using System.Net;
using System.Net.Http.Json;
using Movie.Domain.Lists;
using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// "How many of us have already seen this" — the one legitimate cross-user
/// read in the application. What is under test is as much what these
/// responses omit as what they contain.
/// </summary>
public sealed class WatchSummaryTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    private static readonly object Inception = new
    {
        id = 27205,
        mediaType = "movie",
        title = "Inception",
        year = "2010",
        genres = Array.Empty<string>(),
    };

    [Fact]
    public async Task A_title_nobody_has_seen_is_absent_not_zero()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        var summary = await Summary(owner.Client, listId);

        // The client already treats a missing key as zero, so reporting one
        // explicitly would be a row with nothing to say.
        summary.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_count_is_distinct_members_not_watch_log_entries()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);
        await LogWatchAsync(owner, count: 4);

        var summary = await Summary(owner.Client, listId);

        // Four rewatches by one person is one person, as far as the group is
        // concerned.
        summary.ShouldHaveSingleItem();
        summary[0].WatchedCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_count_grows_with_the_number_of_members_who_have_seen_it()
    {
        var owner = await factory.SignedInAsync();
        var first = await factory.SignedInAsync();
        var second = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        await AddMemberAsync(listId, first.Id);
        await AddMemberAsync(listId, second.Id);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);
        await LogWatchAsync(owner);
        await LogWatchAsync(first);

        var summary = await Summary(owner.Client, listId);

        summary.ShouldHaveSingleItem();
        summary[0].WatchedCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_strangers_viewing_is_not_reported_to_the_list()
    {
        var owner = await factory.SignedInAsync();
        var stranger = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);
        await LogWatchAsync(stranger);

        var summary = await Summary(owner.Client, listId);

        // Having watched the same film is not a relationship. The count is
        // scoped to this list's own accepted members.
        summary.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_removed_members_past_viewing_stops_counting()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var membershipId = await AddMemberAsync(listId, member.Id);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);
        await LogWatchAsync(member);

        (await Summary(owner.Client, listId))[0].WatchedCount.ShouldBe(1);

        await owner.Client.DeleteAsync($"/members/{membershipId}");

        // Membership is checked at read time rather than baked into the row
        // when it was watched, so somebody who has left is not still counted
        // as one of the group.
        (await Summary(owner.Client, listId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_watch_from_before_the_title_was_added_still_counts()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        await LogWatchAsync(owner);

        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        (await Summary(owner.Client, listId))[0].WatchedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Only_members_can_see_the_summary()
    {
        var owner = await factory.SignedInAsync();
        var stranger = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);

        var response = await stranger.Client.GetAsync($"/lists/{listId}/watch-summary");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync($"/lists/{Guid.NewGuid()}/watch-summary"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<SummaryDto[]> Summary(HttpClient client, Guid listId) =>
        (await client.GetFromJsonAsync<SummaryDto[]>($"/lists/{listId}/watch-summary"))!;

    private static async Task LogWatchAsync(MovieApiFactory.SignedInUser user, int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            await user.Client.PostAsJsonAsync("/watch-log", new
            {
                title = Inception,
                watchedAt = DateTime.UtcNow.AddDays(-i),
                rating = (int?)null,
                note = (string?)null,
            });
        }
    }

    private async Task<Guid> CreateListAsync(MovieApiFactory.SignedInUser owner)
    {
        var response = await owner.Client.PostAsJsonAsync("/lists", new { name = "Oscar Winners" });

        return (await response.Content.ReadFromJsonAsync<ListDto>())!.Id;
    }

    private async Task<Guid> AddMemberAsync(Guid listId, Guid userId)
    {
        await using var context = factory.CreateContext();

        var membership = new ListMember
        {
            ListId = listId,
            UserId = userId,
            Status = MemberStatus.Accepted,
            RespondedAt = DateTime.UtcNow,
        };

        context.ListMembers.Add(membership);
        await context.SaveChangesAsync();

        return membership.Id;
    }

    private sealed record ListDto(Guid Id);

    private sealed record SummaryDto(int MediaId, string MediaType, int WatchedCount);
}
