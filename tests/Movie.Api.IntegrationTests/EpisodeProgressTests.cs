using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Episode-level watch state. The table's key is the episode itself, which is
/// what lets marking one episode and marking a season be the same operation.
/// </summary>
public sealed class EpisodeProgressTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    private const int BreakingBad = 1396;

    [Fact]
    public async Task A_marked_episode_comes_back()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PutAsJsonAsync(
            $"/episode-progress/{BreakingBad}/1/1",
            new { watchedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var progress = await Progress(client);

        progress.ShouldHaveSingleItem();
        progress[0].ShowId.ShouldBe(BreakingBad);
        progress[0].SeasonNumber.ShouldBe(1);
        progress[0].EpisodeNumber.ShouldBe(1);
    }

    [Fact]
    public async Task Marking_an_episode_needs_no_body()
    {
        var (client, _) = await factory.SignedInAsync();

        // A caller with nothing to say beyond "I watched this" sends nothing,
        // and the time defaults to now.
        var response = await client.PutAsync($"/episode-progress/{BreakingBad}/1/1", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Progress(client)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Marking_the_same_episode_twice_moves_the_time_rather_than_adding_a_row()
    {
        var (client, _) = await factory.SignedInAsync();
        var later = new DateTime(2025, 5, 5, 0, 0, 0, DateTimeKind.Utc);

        await client.PutAsJsonAsync(
            $"/episode-progress/{BreakingBad}/1/1",
            new { watchedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await client.PutAsJsonAsync($"/episode-progress/{BreakingBad}/1/1", new { watchedAt = later });

        var progress = await Progress(client);

        progress.ShouldHaveSingleItem();
        progress[0].WatchedAt.ShouldBe(later);
    }

    [Fact]
    public async Task A_whole_season_can_be_marked_in_one_request()
    {
        var (client, _) = await factory.SignedInAsync();

        await MarkBatch(client, season: 1, episodes: 7);

        (await Progress(client)).Length.ShouldBe(7);
    }

    [Fact]
    public async Task A_batch_over_episodes_already_marked_updates_them()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PutAsJsonAsync($"/episode-progress/{BreakingBad}/1/3", new
        {
            watchedAt = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        // "Mark everything up to here" runs over episodes some of which are
        // already marked, which is why it is an upsert rather than an insert.
        await MarkBatch(client, season: 1, episodes: 7);

        var progress = await Progress(client);

        progress.Length.ShouldBe(7);
        progress.ShouldAllBe(x => x.WatchedAt.Year == 2024);
    }

    [Fact]
    public async Task A_batch_repeating_an_episode_marks_it_once()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PostAsJsonAsync("/episode-progress/batch", new
        {
            showId = BreakingBad,
            episodes = new[]
            {
                new { seasonNumber = 1, episodeNumber = 1 },
                new { seasonNumber = 1, episodeNumber = 1 },
                new { seasonNumber = 1, episodeNumber = 2 },
            },
            watchedAt = (DateTime?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Progress(client)).Length.ShouldBe(2);
    }

    [Fact]
    public async Task Unmarking_an_episode_leaves_the_rest_of_the_season()
    {
        var (client, _) = await factory.SignedInAsync();
        await MarkBatch(client, season: 1, episodes: 3);

        var response = await client.DeleteAsync($"/episode-progress/{BreakingBad}/1/2");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Progress(client)).Select(x => x.EpisodeNumber).OrderBy(x => x).ShouldBe([1, 3]);
    }

    [Fact]
    public async Task Unmarking_a_season_leaves_the_other_seasons()
    {
        var (client, _) = await factory.SignedInAsync();
        await MarkBatch(client, season: 1, episodes: 3);
        await MarkBatch(client, season: 2, episodes: 2);

        var response = await client.DeleteAsync($"/episode-progress/{BreakingBad}/1");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var left = await Progress(client);
        left.Length.ShouldBe(2);
        left.ShouldAllBe(x => x.SeasonNumber == 2);
    }

    [Fact]
    public async Task Unmarking_what_was_never_marked_still_succeeds()
    {
        var (client, _) = await factory.SignedInAsync();

        (await client.DeleteAsync($"/episode-progress/{BreakingBad}/1/1"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/episode-progress/{BreakingBad}/1"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Two_people_watching_the_same_show_do_not_collide()
    {
        var (mine, _) = await factory.SignedInAsync();
        var (theirs, _) = await factory.SignedInAsync();

        await MarkBatch(mine, season: 1, episodes: 5);
        await MarkBatch(theirs, season: 1, episodes: 2);

        // The key starts with the user, so the same episode of the same show is
        // a different row for each of them.
        (await Progress(mine)).Length.ShouldBe(5);
        (await Progress(theirs)).Length.ShouldBe(2);

        await theirs.DeleteAsync($"/episode-progress/{BreakingBad}/1");

        (await Progress(mine)).Length.ShouldBe(5);
    }

    [Fact]
    public async Task An_oversized_batch_is_refused()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PostAsJsonAsync("/episode-progress/batch", new
        {
            showId = BreakingBad,
            episodes = Enumerable.Range(1, 2001)
                .Select(n => new { seasonNumber = 1, episodeNumber = n })
                .ToArray(),
            watchedAt = (DateTime?)null,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Progress(client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/episode-progress"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Task<HttpResponseMessage> MarkBatch(HttpClient client, int season, int episodes) =>
        client.PostAsJsonAsync("/episode-progress/batch", new
        {
            showId = BreakingBad,
            episodes = Enumerable.Range(1, episodes)
                .Select(n => new { seasonNumber = season, episodeNumber = n })
                .ToArray(),
            watchedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });

    private static async Task<Marked[]> Progress(HttpClient client) =>
        (await client.GetFromJsonAsync<Marked[]>("/episode-progress"))!;

    private sealed record Marked(
        int ShowId,
        int SeasonNumber,
        int EpisodeNumber,
        DateTime WatchedAt);
}
