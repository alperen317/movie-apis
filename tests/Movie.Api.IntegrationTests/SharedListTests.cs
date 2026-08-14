using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Movie.Domain.Lists;
using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Shared lists over HTTP. Invitations are sent in phase 4c, so memberships are
/// written straight to the database here — what is under test is what each
/// state lets somebody reach, not how they got into it.
/// </summary>
public sealed class SharedListTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task A_new_list_belongs_to_whoever_made_it()
    {
        var owner = await factory.SignedInAsync();

        var response = await owner.Client.PostAsJsonAsync("/lists", new { name = "Oscar Winners" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var list = await response.Content.ReadFromJsonAsync<ListDto>();
        list!.Name.ShouldBe("Oscar Winners");
        list.CreatedBy.ShouldBe(owner.Id);
        list.JoinCode.ShouldNotBeNullOrWhiteSpace();

        // The creator is a member of it, written in the same transaction. A
        // list its creator could not read would be no use to anyone.
        var members = await Members(owner.Client, list.Id);
        members.ShouldHaveSingleItem();
        members[0].UserId.ShouldBe(owner.Id);
        members[0].Role.ShouldBe("owner");
        members[0].Status.ShouldBe("accepted");
    }

    [Fact]
    public async Task My_lists_are_the_ones_i_joined()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();

        var joined = await CreateListAsync(owner, "Joined");
        var pending = await CreateListAsync(owner, "Only Invited");
        await AddMemberAsync(joined, member.Id, MemberStatus.Accepted);
        await AddMemberAsync(pending, invitee.Id, MemberStatus.Pending);

        (await MyLists(member.Client)).Select(x => x.Name).ShouldBe(["Joined"]);

        // An invitation is not a list you have. It has its own screen, and
        // blending the two is the reason this reads memberships rather than
        // asking the lists table what is visible.
        (await MyLists(invitee.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invitee_sees_the_name_but_not_the_join_code()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, invitee.Id, MemberStatus.Pending);

        var seen = await invitee.Client.GetFromJsonAsync<ListDto>($"/lists/{listId}");

        seen!.Name.ShouldBe("Oscar Winners");

        // Holding the code is enough to join outright, so handing it to
        // somebody who has not accepted would let them skip the invitation and
        // pass it on to anyone else.
        seen.JoinCode.ShouldBeNull();
    }

    [Fact]
    public async Task An_invitee_cannot_read_the_roster_or_the_contents()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, invitee.Id, MemberStatus.Pending);

        (await invitee.Client.GetAsync($"/lists/{listId}/members"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await invitee.Client.GetAsync($"/lists/{listId}/items"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_stranger_cannot_tell_the_list_exists()
    {
        var owner = await factory.SignedInAsync();
        var stranger = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        // 404 rather than 403 everywhere: a distinct "forbidden" would confirm
        // the list is there to somebody with no business knowing.
        foreach (var path in (string[])["", "/members", "/items"])
        {
            (await stranger.Client.GetAsync($"/lists/{listId}{path}"))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await stranger.Client.PutAsJsonAsync($"/lists/{listId}", new { name = "Mine now" }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.DeleteAsync($"/lists/{listId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Any_member_can_rename_a_list()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        var response = await member.Client.PutAsJsonAsync(
            $"/lists/{listId}",
            new { name = "  Best Picture  " });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var renamed = await owner.Client.GetFromJsonAsync<ListDto>($"/lists/{listId}");

        // Stored trimmed, which is also how the length was measured.
        renamed!.Name.ShouldBe("Best Picture");

        // Renaming is not a way to take the list over — see the tamper guard.
        renamed.CreatedBy.ShouldBe(owner.Id);
    }

    [Fact]
    public async Task Only_the_creator_can_delete_a_list()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        (await member.Client.DeleteAsync($"/lists/{listId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.Client.DeleteAsync($"/lists/{listId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MyLists(member.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_a_list_takes_its_members_and_items_with_it()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        await owner.Client.DeleteAsync($"/lists/{listId}");

        await using var context = factory.CreateContext();
        (await context.ListMembers.CountAsync(x => x.ListId == listId)).ShouldBe(0);
        (await context.ListItems.CountAsync(x => x.ListId == listId)).ShouldBe(0);
    }

    [Fact]
    public async Task The_roster_shows_who_has_not_answered_yet()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);
        await AddMemberAsync(listId, invitee.Id, MemberStatus.Pending);

        var roster = await Members(member.Client, listId);

        // Unanswered invitations are on the roster on purpose, so the members
        // screen can show who is still deciding.
        roster.Length.ShouldBe(3);
        roster.Single(x => x.UserId == invitee.Id).Status.ShouldBe("pending");
        roster.Single(x => x.UserId == invitee.Id).Email.ShouldBe(invitee.Email);
    }

    [Fact]
    public async Task The_creator_can_remove_somebody_and_anybody_can_leave()
    {
        var owner = await factory.SignedInAsync();
        var first = await factory.SignedInAsync();
        var second = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, first.Id, MemberStatus.Accepted);
        await AddMemberAsync(listId, second.Id, MemberStatus.Accepted);

        var roster = await Members(owner.Client, listId);
        var firstId = roster.Single(x => x.UserId == first.Id).MembershipId;
        var secondId = roster.Single(x => x.UserId == second.Id).MembershipId;

        // One member cannot throw another out. Only the creator can, and each
        // person can leave.
        (await second.Client.DeleteAsync($"/members/{firstId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.Client.DeleteAsync($"/members/{firstId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await second.Client.DeleteAsync($"/members/{secondId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Members(owner.Client, listId)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task The_creator_cannot_leave_their_own_list()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        var ownerMembership = (await Members(owner.Client, listId))[0].MembershipId;

        var response = await owner.Client.DeleteAsync($"/members/{ownerMembership}");

        // Ownership is read off the list's creator column rather than a
        // membership row, so a creator who left would still be the only one
        // able to delete the list while no longer being able to read it.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await Members(owner.Client, listId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_title_added_twice_stays_one_item()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);

        var first = await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);
        var second = await member.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var added = await first.Content.ReadFromJsonAsync<ItemDto>();
        var again = await second.Content.ReadFromJsonAsync<ItemDto>();

        // The second caller gets the row that is there, which also tells them
        // who put it there — the question they would ask next.
        again!.RowId.ShouldBe(added!.RowId);
        again.AddedBy.ShouldBe(owner.Id);

        (await Items(member.Client, listId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Any_member_can_remove_any_title()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await AddMemberAsync(listId, member.Id, MemberStatus.Accepted);
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        // Removal does not follow from who added it: members are equals when
        // editing content, and the adder is recorded only to be displayed.
        var response = await member.Client.DeleteAsync($"/lists/{listId}/items/movie/27205");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Items(owner.Client, listId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_item_carries_the_name_of_whoever_added_it()
    {
        var owner = await factory.SignedInAsync();
        await owner.Client.PutAsJsonAsync("/me", new { displayName = "Alperen", avatarVariant = "beam" });
        var listId = await CreateListAsync(owner, "Oscar Winners");

        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        var item = (await Items(owner.Client, listId))[0];
        item.AddedByName.ShouldBe("Alperen");
        item.AddedByAvatarVariant.ShouldBe("beam");
    }

    [Fact]
    public async Task A_stranger_cannot_add_or_remove_titles()
    {
        var owner = await factory.SignedInAsync();
        var stranger = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await owner.Client.PostAsJsonAsync($"/lists/{listId}/items", Inception);

        (await stranger.Client.PostAsJsonAsync($"/lists/{listId}/items", Arrival))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await stranger.Client.DeleteAsync($"/lists/{listId}/items/movie/27205"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Items(owner.Client, listId)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_list_needs_a_name_that_fits()
    {
        var owner = await factory.SignedInAsync();

        (await owner.Client.PostAsJsonAsync("/lists", new { name = "   " }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Measured after trimming, the same as the check constraint does.
        (await owner.Client.PostAsJsonAsync("/lists", new { name = $"  {new string('a', 61)}  " }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await owner.Client.PostAsJsonAsync("/lists", new { name = $"  {new string('a', 60)}  " }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Every_list_gets_its_own_join_code()
    {
        var owner = await factory.SignedInAsync();

        await CreateListAsync(owner, "First");
        await CreateListAsync(owner, "Second");

        var codes = (await MyLists(owner.Client)).Select(x => x.JoinCode).ToArray();

        codes.ShouldAllBe(code => code!.Length == 8);
        codes.Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/lists")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/lists", new { name = "Mine" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> CreateListAsync(MovieApiFactory.SignedInUser owner, string name)
    {
        var response = await owner.Client.PostAsJsonAsync("/lists", new { name });

        return (await response.Content.ReadFromJsonAsync<ListDto>())!.Id;
    }

    /// <summary>
    /// Written straight to the database, because sending an invitation is
    /// phase 4c. What these tests are about is what a membership in a given
    /// state permits.
    /// </summary>
    private async Task AddMemberAsync(Guid listId, Guid userId, MemberStatus status)
    {
        await using var context = factory.CreateContext();

        context.ListMembers.Add(new ListMember
        {
            ListId = listId,
            UserId = userId,
            Status = status,
            RespondedAt = status is MemberStatus.Accepted ? DateTime.UtcNow : null,
        });

        await context.SaveChangesAsync();
    }

    private static async Task<ListDto[]> MyLists(HttpClient client) =>
        (await client.GetFromJsonAsync<ListDto[]>("/lists"))!;

    private static async Task<MemberDto[]> Members(HttpClient client, Guid listId) =>
        (await client.GetFromJsonAsync<MemberDto[]>($"/lists/{listId}/members"))!;

    private static async Task<ItemDto[]> Items(HttpClient client, Guid listId) =>
        (await client.GetFromJsonAsync<ItemDto[]>($"/lists/{listId}/items"))!;

    private static readonly object Inception = new
    {
        id = 27205,
        mediaType = "movie",
        title = "Inception",
        posterPath = "/inception.jpg",
        voteAverage = 8.4m,
        year = "2010",
        genres = new[] { "Action" },
    };

    private static readonly object Arrival = new
    {
        id = 329865,
        mediaType = "movie",
        title = "Arrival",
        posterPath = "/arrival.jpg",
        voteAverage = 7.6m,
        year = "2016",
        genres = new[] { "Drama" },
    };

    private sealed record ListDto(
        Guid Id,
        string Name,
        Guid CreatedBy,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string? JoinCode);

    private sealed record MemberDto(
        Guid MembershipId,
        Guid ListId,
        Guid UserId,
        string Email,
        string? DisplayName,
        string AvatarVariant,
        string? AvatarSeed,
        string Role,
        string Status,
        Guid? InvitedBy,
        DateTime CreatedAt,
        DateTime? RespondedAt);

    private sealed record ItemDto(
        Guid RowId,
        Guid ListId,
        int Id,
        string MediaType,
        string Title,
        string? PosterPath,
        decimal? VoteAverage,
        string? Year,
        string[] Genres,
        Guid AddedBy,
        string AddedByName,
        string AddedByAvatarVariant,
        string? AddedByAvatarSeed,
        DateTime AddedAt);
}
