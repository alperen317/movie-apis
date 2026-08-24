using System.Net;
using System.Net.Http.Json;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// The counting that stands in for the <c>invite_attempts</c> and
/// <c>join_attempts</c> tables.
/// </summary>
/// <remarks>
/// Both endpoints answer the same way whether or not the thing being asked
/// about exists, which leaves an attacker with volume and nothing else. This is
/// what takes the volume away.
/// </remarks>
public sealed class InvitationRateLimitTests(ThrottledListApiFactory factory)
    : IClassFixture<ThrottledListApiFactory>
{
    [Fact]
    public async Task Probing_addresses_runs_out_even_though_none_of_them_land()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);

        var answers = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await owner.Client.PostAsJsonAsync(
                $"/lists/{listId}/invites",
                new { email = $"{Guid.NewGuid():N}@example.com" });

            answers.Add(response.StatusCode);
        }

        // Attempts are counted, not successes. If a failed probe were free,
        // the limit would not bound the scan it exists to bound.
        answers[0].ShouldBe(HttpStatusCode.Conflict);
        answers[1].ShouldBe(HttpStatusCode.Conflict);
        answers[2].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task One_persons_spending_does_not_touch_anothers()
    {
        var first = await factory.SignedInAsync();
        var second = await factory.SignedInAsync();
        var theirList = await CreateListAsync(first);
        var ourList = await CreateListAsync(second);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await first.Client.PostAsJsonAsync(
                $"/lists/{theirList}/invites",
                new { email = $"{Guid.NewGuid():N}@example.com" });
        }

        var mine = await second.Client.PostAsJsonAsync(
            $"/lists/{ourList}/invites",
            new { email = $"{Guid.NewGuid():N}@example.com" });

        // Both requests come from the same loopback address, so this passing is
        // what shows the budget is counted per account. Counting the host would
        // let one account spend everybody else's, and let an attacker refill by
        // moving.
        mine.StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Guessing_codes_runs_out()
    {
        var guesser = await factory.SignedInAsync();

        var answers = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await guesser.Client.PostAsJsonAsync(
                "/lists/join",
                new { code = $"ZZZZZZZ{attempt}" });

            answers.Add(response.StatusCode);
        }

        answers[0].ShouldBe(HttpStatusCode.NotFound);
        answers[1].ShouldBe(HttpStatusCode.NotFound);
        answers[2].ShouldBe(HttpStatusCode.TooManyRequests);
    }

    private static async Task<Guid> CreateListAsync(MovieApiFactory.SignedInUser owner)
    {
        var response = await owner.Client.PostAsJsonAsync("/lists", new { name = "Oscar Winners" });

        return (await response.Content.ReadFromJsonAsync<ListDto>())!.Id;
    }

    private sealed record ListDto(Guid Id);
}