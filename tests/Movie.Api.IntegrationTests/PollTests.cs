using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;

using Movie.Domain.Lists;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// "What are we watching tonight." A poll has no stored open/closed flag — it
/// is open exactly while its deadline is in the future — so several of these
/// tests move the deadline in the database rather than waiting for real time
/// to pass.
/// </summary>
public sealed class PollTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task A_list_with_no_poll_yet_says_so_without_pretending_not_to_exist()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);

        var response = await owner.Client.GetAsync($"/lists/{listId}/poll");

        // Distinct from the list itself being out of reach: only one of the
        // two means the caller should stop asking.
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_started_poll_can_be_read_back()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);

        var started = await StartPoll(owner, listId, [inception, arrival]);

        started.StatusCode.ShouldBe(HttpStatusCode.Created);

        var poll = await owner.Client.GetFromJsonAsync<PollDto>($"/lists/{listId}/poll");

        poll!.CreatedBy.ShouldBe(owner.Id);
        poll.Candidates.Select(x => x.ListItemId).ShouldBe([inception, arrival], ignoreOrder: true);
        poll.Candidates.ShouldAllBe(x => x.VoteCount == 0 && !x.MyVote);
    }

    [Fact]
    public async Task Any_member_can_start_a_poll()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        await AddMemberAsync(listId, member.Id);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);

        var response = await StartPoll(member, listId, [inception, arrival]);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_deadline_that_has_already_passed_is_refused()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);

        var response = await owner.Client.PostAsJsonAsync($"/lists/{listId}/polls", new
        {
            deadline = DateTime.UtcNow.AddDays(-1),
            itemIds = new[] { inception, arrival },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_deadline");
    }

    [Fact]
    public async Task One_candidate_is_not_a_vote()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, _) = await AddTwoItemsAsync(owner, listId);

        var response = await StartPoll(owner, listId, [inception]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("need_at_least_two_candidates");
    }

    [Fact]
    public async Task Naming_the_same_item_twice_still_needs_a_second_real_candidate()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, _) = await AddTwoItemsAsync(owner, listId);

        var response = await StartPoll(owner, listId, [inception, inception]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("need_at_least_two_candidates");
    }

    [Fact]
    public async Task A_candidate_from_another_list_is_refused()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var otherListId = await CreateListAsync(owner);
        var (inception, _) = await AddTwoItemsAsync(owner, listId);
        var (elsewhere, _) = await AddTwoItemsAsync(owner, otherListId);

        // The Supabase function never checked this: its foreign key only
        // established that the item existed somewhere, not that it belonged to
        // the list the poll is being started on.
        var response = await StartPoll(owner, listId, [inception, elsewhere]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_candidate");
    }

    [Fact]
    public async Task Only_one_poll_runs_at_a_time()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);
        await StartPoll(owner, listId, [inception, arrival]);

        var second = await StartPoll(owner, listId, [inception, arrival]);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).ShouldContain("poll_already_active");
    }

    [Fact]
    public async Task A_finished_poll_does_not_block_the_next_one()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);
        var firstId = await StartAndGetIdAsync(owner, listId, inception, arrival);
        await CloseAsync(firstId);

        var second = await StartPoll(owner, listId, [inception, arrival]);

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_vote_is_counted()
    {
        var owner = await factory.SignedInAsync();
        var voter = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        await AddMemberAsync(listId, voter.Id);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);
        var pollId = await StartAndGetIdAsync(owner, listId, inception, arrival);
        var candidateId = (await Poll(owner, listId))!.Candidates
            .Single(x => x.ListItemId == inception).Id;

        var response = await voter.Client.PostAsJsonAsync(
            $"/polls/{pollId}/votes",
            new { candidateId });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var seenByOwner = (await Poll(owner, listId))!.Candidates.Single(x => x.Id == candidateId);
        seenByOwner.VoteCount.ShouldBe(1);
        seenByOwner.MyVote.ShouldBeFalse();

        var seenByVoter = (await Poll(voter, listId))!.Candidates.Single(x => x.Id == candidateId);
        seenByVoter.MyVote.ShouldBeTrue();
    }

    [Fact]
    public async Task Changing_your_mind_moves_the_vote_rather_than_adding_one()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);
        await StartAndGetIdAsync(owner, listId, inception, arrival);
        var poll = (await Poll(owner, listId))!;
        var first = poll.Candidates.Single(x => x.ListItemId == inception).Id;
        var second = poll.Candidates.Single(x => x.ListItemId == arrival).Id;

        await owner.Client.PostAsJsonAsync($"/polls/{poll.Id}/votes", new { candidateId = first });
        await owner.Client.PostAsJsonAsync($"/polls/{poll.Id}/votes", new { candidateId = second });

        var final = (await Poll(owner, listId))!;
        final.Candidates.Single(x => x.Id == first).VoteCount.ShouldBe(0);
        final.Candidates.Single(x => x.Id == second).VoteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Voting_after_the_deadline_is_refused()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);
        var pollId = await StartAndGetIdAsync(owner, listId, inception, arrival);
        var candidateId = (await Poll(owner, listId))!.Candidates[0].Id;
        await CloseAsync(pollId);

        var response = await owner.Client.PostAsJsonAsync(
            $"/polls/{pollId}/votes",
            new { candidateId });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("poll_closed");
    }

    [Fact]
    public async Task A_candidate_from_a_different_poll_is_refused()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var otherListId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);
        var (city, town) = await AddTwoItemsAsync(owner, otherListId);
        var pollId = await StartAndGetIdAsync(owner, listId, inception, arrival);
        await StartPoll(owner, otherListId, [city, town]);
        var foreignCandidateId = (await Poll(owner, otherListId))!.Candidates[0].Id;

        var response = await owner.Client.PostAsJsonAsync(
            $"/polls/{pollId}/votes",
            new { candidateId = foreignCandidateId });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_candidate");
    }

    [Fact]
    public async Task Somebody_outside_the_list_cannot_start_a_poll_or_vote()
    {
        var owner = await factory.SignedInAsync();
        var stranger = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner);
        var (inception, arrival) = await AddTwoItemsAsync(owner, listId);
        var pollId = await StartAndGetIdAsync(owner, listId, inception, arrival);
        var candidateId = (await Poll(owner, listId))!.Candidates[0].Id;

        (await StartPoll(stranger, listId, [inception, arrival]))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.PostAsJsonAsync($"/polls/{pollId}/votes", new { candidateId }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.GetAsync($"/lists/{listId}/poll"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync($"/lists/{Guid.NewGuid()}/poll"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpResponseMessage> StartPoll(
        MovieApiFactory.SignedInUser caller,
        Guid listId,
        Guid[] itemIds) =>
        await caller.Client.PostAsJsonAsync($"/lists/{listId}/polls", new
        {
            deadline = DateTime.UtcNow.AddDays(1),
            itemIds,
        });

    private static async Task<Guid> StartAndGetIdAsync(
        MovieApiFactory.SignedInUser owner,
        Guid listId,
        Guid inception,
        Guid arrival)
    {
        var response = await StartPoll(owner, listId, [inception, arrival]);

        return (await response.Content.ReadFromJsonAsync<StartedDto>())!.PollId;
    }

    private static Task<PollDto?> Poll(MovieApiFactory.SignedInUser caller, Guid listId) =>
        caller.Client.GetFromJsonAsync<PollDto>($"/lists/{listId}/poll");

    /// <summary>Moves a poll's deadline into the past without waiting for real time.</summary>
    private async Task CloseAsync(Guid pollId)
    {
        await using var context = factory.CreateContext();

        await context.Database.ExecuteSqlAsync(
            $"update list_polls set deadline = now() - interval '1 hour' where id = {pollId}");
    }

    private async Task<Guid> CreateListAsync(MovieApiFactory.SignedInUser owner)
    {
        var response = await owner.Client.PostAsJsonAsync("/lists", new { name = "Oscar Winners" });

        return (await response.Content.ReadFromJsonAsync<ListDto>())!.Id;
    }

    private async Task AddMemberAsync(Guid listId, Guid userId)
    {
        await using var context = factory.CreateContext();

        context.ListMembers.Add(new ListMember
        {
            ListId = listId,
            UserId = userId,
            Status = MemberStatus.Accepted,
            RespondedAt = DateTime.UtcNow,
        });

        await context.SaveChangesAsync();
    }

    private static async Task<(Guid Inception, Guid Arrival)> AddTwoItemsAsync(
        MovieApiFactory.SignedInUser owner,
        Guid listId)
    {
        var inception = await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", new
        {
            id = 27205,
            mediaType = "movie",
            title = "Inception",
            year = "2010",
            genres = Array.Empty<string>(),
        });

        var arrival = await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", new
        {
            id = 329865,
            mediaType = "movie",
            title = "Arrival",
            year = "2016",
            genres = Array.Empty<string>(),
        });

        return (
            (await inception.Content.ReadFromJsonAsync<ItemDto>())!.RowId,
            (await arrival.Content.ReadFromJsonAsync<ItemDto>())!.RowId);
    }

    private sealed record ListDto(Guid Id);

    private sealed record ItemDto(Guid RowId);

    private sealed record StartedDto(Guid PollId);

    private sealed record PollDto(
        Guid Id,
        DateTime Deadline,
        Guid CreatedBy,
        PollCandidateDto[] Candidates);

    private sealed record PollCandidateDto(Guid Id, Guid ListItemId, int VoteCount, bool MyVote);
}