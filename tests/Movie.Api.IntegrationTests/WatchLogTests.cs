using System.Net;
using System.Net.Http.Json;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// The diary. What separates it from saved media is that a title may appear
/// many times, so entries are addressed by their own id.
/// </summary>
public sealed class WatchLogTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task A_logged_watch_comes_back_with_an_id()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PostAsJsonAsync("/watch-log", Watched(Inception, rating: 9));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var written = await response.Content.ReadFromJsonAsync<Entry>();

        // The id is the point of handing the entry back: the caller cannot work
        // it out from the title, and needs it to edit or delete the entry.
        written!.LogId.ShouldNotBe(Guid.Empty);
        written.Id.ShouldBe(27205);
        written.Rating.ShouldBe(9);

        var log = await Log(client);
        log.ShouldHaveSingleItem();
        log[0].LogId.ShouldBe(written.LogId);
    }

    [Fact]
    public async Task A_rewatch_is_a_second_entry()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PostAsJsonAsync("/watch-log", Watched(Inception, when: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await client.PostAsJsonAsync("/watch-log", Watched(Inception, when: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)));

        // No unique index, on purpose: watching something twice is two events,
        // not one recorded twice.
        var log = await Log(client);

        log.Length.ShouldBe(2);
        log.Select(x => x.LogId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task The_diary_is_ordered_by_when_the_watch_happened()
    {
        var (client, _) = await factory.SignedInAsync();

        // Written oldest-first, and backdated, so the order that comes back
        // cannot be the order they were recorded in.
        await client.PostAsJsonAsync("/watch-log", Watched(Inception, when: new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)));
        await client.PostAsJsonAsync("/watch-log", Watched(Arrival, when: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        (await Log(client)).Select(x => x.Title).ShouldBe(["Arrival", "Inception"]);
    }

    [Fact]
    public async Task An_entry_can_be_corrected()
    {
        var (client, _) = await factory.SignedInAsync();
        var written = await Write(client, Watched(Inception, rating: 4));

        var response = await client.PutAsJsonAsync($"/watch-log/{written.LogId}", new
        {
            watchedAt = new DateTime(2023, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            rating = 8,
            note = "Better the second time.",
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = (await Log(client))[0];
        updated.Rating.ShouldBe(8);
        updated.Note.ShouldBe("Better the second time.");

        // The title is not editable: what is corrected is the record of
        // watching something, not which thing was watched.
        updated.Title.ShouldBe("Inception");
    }

    [Fact]
    public async Task A_rating_can_be_taken_back()
    {
        var (client, _) = await factory.SignedInAsync();
        var written = await Write(client, Watched(Inception, rating: 4));

        await client.PutAsJsonAsync($"/watch-log/{written.LogId}", new
        {
            watchedAt = written.WatchedAt,
            rating = (int?)null,
            note = (string?)null,
        });

        (await Log(client))[0].Rating.ShouldBeNull();
    }

    [Fact]
    public async Task A_rating_outside_one_to_ten_is_refused()
    {
        var (client, _) = await factory.SignedInAsync();

        // Kept in step with the check constraint, so this is a 400 rather than
        // a failed write.
        (await client.PostAsJsonAsync("/watch-log", Watched(Inception, rating: 11)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/watch-log", Watched(Inception, rating: 0)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_time_with_no_zone_is_refused_rather_than_guessed_at()
    {
        var (client, _) = await factory.SignedInAsync();

        // Serialises as "2024-01-01T20:00:00" with nothing after it, which is a
        // different instant for every reader. Guessing would quietly move the
        // entry by hours for anyone not on UTC.
        var response = await client.PostAsJsonAsync(
            "/watch-log",
            Watched(Inception, when: new DateTime(2024, 1, 1, 20, 0, 0, DateTimeKind.Unspecified)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_offset_other_than_utc_is_understood()
    {
        var (client, _) = await factory.SignedInAsync();

        // "2024-01-01T20:00:00+03:00" names an instant just as well as a Z
        // does, so it is converted rather than refused.
        await client.PostAsJsonAsync("/watch-log", Watched(
            Inception,
            when: new DateTimeOffset(2024, 1, 1, 20, 0, 0, TimeSpan.FromHours(3)).LocalDateTime));

        (await Log(client))[0].WatchedAt
            .ShouldBe(new DateTime(2024, 1, 1, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Every_entry_for_a_title_goes_at_once()
    {
        var (client, _) = await factory.SignedInAsync();
        var first = await Write(client, Watched(Inception, when: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        var second = await Write(client, Watched(Inception, when: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await Write(client, Watched(Arrival));

        var response = await Delete(client, first.LogId, second.LogId);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Unmarking a title as watched has to take every entry with it: the
        // mark means "is there any entry at all", so a rewatch left behind
        // would keep the title looking watched.
        (await Log(client)).Select(x => x.Title).ShouldBe(["Arrival"]);
    }

    [Fact]
    public async Task Another_persons_entry_cannot_be_edited_or_deleted()
    {
        var (mine, _) = await factory.SignedInAsync();
        var (theirs, _) = await factory.SignedInAsync();
        var written = await Write(mine, Watched(Inception, rating: 5));

        var edit = await theirs.PutAsJsonAsync($"/watch-log/{written.LogId}", new
        {
            watchedAt = written.WatchedAt,
            rating = 1,
            note = "not mine",
        });

        // Not there rather than forbidden, which is also the only answer that
        // does not confirm the entry exists.
        edit.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await Delete(theirs, written.LogId);

        var untouched = (await Log(mine))[0];
        untouched.Rating.ShouldBe(5);
        untouched.Note.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_entry_is_not_found()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PutAsJsonAsync($"/watch-log/{Guid.NewGuid()}", new
        {
            watchedAt = DateTime.UtcNow,
            rating = (int?)null,
            note = (string?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_import_writes_every_watch_it_is_given()
    {
        var (client, _) = await factory.SignedInAsync();

        var history = Enumerable.Range(1, 40)
            .Select(n => Watched(
                new TitlePayload(n, "movie", $"Film {n}", null, null, "2020", []),
                when: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(n)))
            .ToArray();

        var response = await client.PostAsJsonAsync("/watch-log/batch", history);

        (await response.Content.ReadFromJsonAsync<BatchResult>())!.Logged.ShouldBe(40);

        // Run twice on purpose: unlike saved media there is nothing to skip,
        // because a repeated import is indistinguishable from real rewatches.
        await client.PostAsJsonAsync("/watch-log/batch", history);

        (await Log(client)).Length.ShouldBe(80);
    }

    [Fact]
    public async Task An_oversized_batch_is_refused()
    {
        var (client, _) = await factory.SignedInAsync();

        var tooMany = Enumerable.Range(1, 501)
            .Select(n => Watched(new TitlePayload(n, "movie", $"Film {n}", null, null, "2020", [])))
            .ToArray();

        (await client.PostAsJsonAsync("/watch-log/batch", tooMany))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await Log(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/watch-log")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<Entry> Write(HttpClient client, object watch)
    {
        var response = await client.PostAsJsonAsync("/watch-log", watch);

        return (await response.Content.ReadFromJsonAsync<Entry>())!;
    }

    private static Task<HttpResponseMessage> Delete(HttpClient client, params Guid[] ids) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/watch-log")
        {
            Content = JsonContent.Create(new { ids }),
        });

    private static async Task<Entry[]> Log(HttpClient client) =>
        (await client.GetFromJsonAsync<Entry[]>("/watch-log"))!;

    private static object Watched(TitlePayload title, DateTime? when = null, int? rating = null) =>
        new
        {
            title,
            watchedAt = when ?? new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            rating,
            note = (string?)null,
        };

    private static readonly TitlePayload Inception = new(
        27205, "movie", "Inception", "/inception.jpg", 8.4m, "2010", ["Action"]);

    private static readonly TitlePayload Arrival = new(
        329865, "movie", "Arrival", "/arrival.jpg", 7.6m, "2016", ["Drama"]);

    private sealed record TitlePayload(
        int Id,
        string MediaType,
        string Title,
        string? PosterPath,
        decimal? VoteAverage,
        string? Year,
        string[] Genres);

    private sealed record BatchResult(int Logged);

    private sealed record Entry(
        Guid LogId,
        int Id,
        string MediaType,
        string Title,
        string? PosterPath,
        decimal? VoteAverage,
        string? Year,
        string[] Genres,
        DateTime WatchedAt,
        int? Rating,
        string? Note);
}