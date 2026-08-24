using System.Net;
using System.Net.Http.Json;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// "Not interested." The smallest table in the schema, and the one with the
/// least to say: a dismissal either exists or it does not.
/// </summary>
public sealed class RecommendationFeedbackTests(MovieApiFactory factory)
    : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task A_dismissed_title_comes_back()
    {
        var (client, _) = await factory.SignedInAsync();

        var response = await client.PostAsJsonAsync(
            "/recommendation-feedback",
            new { mediaId = 27205, mediaType = "movie" });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var dismissed = await Dismissed(client);

        dismissed.ShouldHaveSingleItem();
        dismissed[0].MediaId.ShouldBe(27205);
        dismissed[0].MediaType.ShouldBe("movie");
    }

    [Fact]
    public async Task Dismissing_the_same_title_twice_is_not_an_error()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PostAsJsonAsync(
            "/recommendation-feedback",
            new { mediaId = 1399, mediaType = "tv" });

        var again = await client.PostAsJsonAsync(
            "/recommendation-feedback",
            new { mediaId = 1399, mediaType = "tv" });

        // The unique index refuses the second row and that refusal is caught,
        // because the outcome the caller asked for — the title stays hidden —
        // already holds.
        again.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Dismissed(client)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task A_film_and_a_show_sharing_an_id_are_different_titles()
    {
        var (client, _) = await factory.SignedInAsync();

        await client.PostAsJsonAsync(
            "/recommendation-feedback",
            new { mediaId = 1399, mediaType = "movie" });
        await client.PostAsJsonAsync(
            "/recommendation-feedback",
            new { mediaId = 1399, mediaType = "tv" });

        // TMDB numbers films and shows separately, so the kind is part of the
        // key rather than a label on it.
        (await Dismissed(client)).Length.ShouldBe(2);
    }

    [Fact]
    public async Task One_persons_dismissals_are_invisible_to_another()
    {
        var (mine, _) = await factory.SignedInAsync();
        var (theirs, _) = await factory.SignedInAsync();

        await mine.PostAsJsonAsync(
            "/recommendation-feedback",
            new { mediaId = 27205, mediaType = "movie" });

        (await Dismissed(theirs)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/recommendation-feedback"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<DismissedItem[]> Dismissed(HttpClient client) =>
        (await client.GetFromJsonAsync<DismissedItem[]>("/recommendation-feedback"))!;

    private sealed record DismissedItem(int MediaId, string MediaType);
}