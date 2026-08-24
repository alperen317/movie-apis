using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Favorites and the watchlist, over HTTP. What is being checked here is not
/// only that a title comes back, but that it comes back to the person who saved
/// it and nobody else.
/// </summary>
public sealed class SavedMediaTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task A_saved_title_comes_back()
    {
        var (client, _) = await factory.SignedInAsync();

        var saved = await client.PostAsJsonAsync("/saved-media?listType=favorite", Inception);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var favorites = await client.GetFromJsonAsync<SavedItem[]>("/saved-media?listType=favorite");

        favorites.ShouldHaveSingleItem();
        favorites[0].Id.ShouldBe(27205);
        favorites[0].MediaType.ShouldBe("movie");
        favorites[0].Title.ShouldBe("Inception");
        favorites[0].Genres.ShouldBe(["Action", "Science Fiction"]);
    }

    [Fact]
    public async Task The_two_lists_are_separate()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PostAsJsonAsync("/saved-media?listType=favorite", Inception);
        await client.PostAsJsonAsync("/saved-media?listType=watchlist", Inception);

        // The unique index spans the list type, so the same film sits in both
        // at once — which is the point of having two.
        (await Favorites(client)).Length.ShouldBe(1);
        (await client.GetFromJsonAsync<SavedItem[]>("/saved-media?listType=watchlist"))!
            .Length.ShouldBe(1);

        await client.DeleteAsync("/saved-media/movie/27205?listType=favorite");

        (await Favorites(client)).ShouldBeEmpty();
        (await client.GetFromJsonAsync<SavedItem[]>("/saved-media?listType=watchlist"))!
            .Length.ShouldBe(1);
    }

    [Fact]
    public async Task The_list_is_newest_first()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PostAsJsonAsync("/saved-media?listType=favorite", Inception);
        await client.PostAsJsonAsync("/saved-media?listType=favorite", Arrival);

        // Backdated in SQL rather than relying on two requests landing on
        // different clock ticks, which they need not do.
        await using (var context = factory.CreateContext())
        {
            await context.Database.ExecuteSqlAsync(
                $"update saved_media set created_at = now() - interval '1 day' where media_id = 27205");
        }

        var favorites = await Favorites(client);

        favorites.Select(x => x.Title).ShouldBe(["Arrival", "Inception"]);
    }

    [Fact]
    public async Task Saving_the_same_title_twice_changes_nothing()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PostAsJsonAsync("/saved-media?listType=favorite", Inception);
        var again = await client.PostAsJsonAsync("/saved-media?listType=favorite", Inception);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Not an error, but not silent either: zero is how the caller learns
        // its local state was already behind.
        (await again.Content.ReadFromJsonAsync<SaveResult>())!.Saved.ShouldBe(0);
        (await Favorites(client)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Removing_a_title_that_is_not_there_still_succeeds()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.DeleteAsync("/saved-media/movie/27205?listType=favorite");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task One_persons_library_is_invisible_to_another()
    {
        var (mine, _) = await factory.SignedInAsync();
        var (theirs, _) = await factory.SignedInAsync();

        await mine.PostAsJsonAsync("/saved-media?listType=favorite", Inception);

        (await Favorites(theirs)).ShouldBeEmpty();

        // And the delete cannot reach across either: it runs through the same
        // ownership filter as the read.
        await theirs.DeleteAsync("/saved-media/movie/27205?listType=favorite");

        (await Favorites(mine)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/saved-media?listType=favorite"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_import_can_be_run_twice()
    {
        var (client, _) = await factory.SignedInAsync();
        var library = Enumerable.Range(1, 50).Select(TitleNumber).ToArray();

        var first = await client.PostAsJsonAsync("/saved-media/batch?listType=watchlist", library);
        var second = await client.PostAsJsonAsync("/saved-media/batch?listType=watchlist", library);

        (await first.Content.ReadFromJsonAsync<SaveResult>())!.Saved.ShouldBe(50);

        // The whole point of the importer's conflict handling: re-running it
        // after a half-finished attempt skips what landed rather than failing.
        (await second.Content.ReadFromJsonAsync<SaveResult>())!.Saved.ShouldBe(0);

        (await client.GetFromJsonAsync<SavedItem[]>("/saved-media?listType=watchlist"))!
            .Length.ShouldBe(50);
    }

    [Fact]
    public async Task A_batch_that_repeats_itself_saves_the_title_once()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PostAsJsonAsync(
            "/saved-media/batch?listType=favorite",
            new[] { Inception, Inception, Arrival });

        (await response.Content.ReadFromJsonAsync<SaveResult>())!.Saved.ShouldBe(2);
        (await Favorites(client)).Length.ShouldBe(2);
    }

    [Fact]
    public async Task An_oversized_batch_is_refused_rather_than_trimmed()
    {
        var (client, _) = await factory.SignedInAsync();
        var tooMany = Enumerable.Range(1, 501).Select(TitleNumber).ToArray();

        var response = await client.PostAsJsonAsync("/saved-media/batch?listType=favorite", tooMany);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Refused whole. A partially applied import that reported success would
        // be worse than one that failed.
        (await Favorites(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_list_type_that_does_not_exist_is_refused()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.GetAsync("/saved-media?listType=starred");

        // The lower-case spellings the client writes are read case-insensitively
        // on purpose, but only into values that exist.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_overlong_title_is_refused()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PostAsJsonAsync(
            "/saved-media?listType=favorite",
            Inception with { Title = new string('a', 501) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_untagged_title_is_saved_with_no_genres()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PostAsJsonAsync(
            "/saved-media?listType=favorite",
            new { id = 999, mediaType = "tv", title = "Something Obscure" });

        // The column is not null, so an omitted array has to become an empty
        // one rather than a refusal — an untagged title is perfectly ordinary.
        (await Favorites(client))[0].Genres.ShouldBeEmpty();
    }

    private static async Task<SavedItem[]> Favorites(HttpClient client) =>
        (await client.GetFromJsonAsync<SavedItem[]>("/saved-media?listType=favorite"))!;

    private static readonly SaveRequest Inception = new(
        27205, "movie", "Inception", "/inception.jpg", 8.4m, "2010",
        ["Action", "Science Fiction"]);

    private static readonly SaveRequest Arrival = new(
        329865, "movie", "Arrival", "/arrival.jpg", 7.6m, "2016", ["Drama"]);

    private static SaveRequest TitleNumber(int n) =>
        new(n, "movie", $"Film {n}", null, null, "2020", []);

    private sealed record SaveRequest(
        int Id,
        string MediaType,
        string Title,
        string? PosterPath,
        decimal? VoteAverage,
        string? Year,
        string[] Genres);

    private sealed record SaveResult(int Saved);

    private sealed record SavedItem(
        int Id,
        string MediaType,
        string Title,
        string? PosterPath,
        decimal? VoteAverage,
        string? Year,
        string[] Genres,
        DateTime SavedAt);
}