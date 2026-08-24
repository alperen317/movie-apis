using System.Net;
using System.Net.Http.Json;

using Microsoft.EntityFrameworkCore;

using Movie.Domain.Lists;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Invitations and joining by code.
/// </summary>
/// <remarks>
/// The tests that matter most here are the ones checking that two different
/// situations produce the <em>same</em> answer. An invitation endpoint that
/// says "no account with that address" is an oracle for who has registered,
/// and the list is one the asker controls, so they can ask about anybody.
/// </remarks>
public sealed class InvitationTests(MovieApiFactory factory) : IClassFixture<MovieApiFactory>
{
    [Fact]
    public async Task An_invitation_shows_up_for_the_person_invited()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        var sent = await Invite(owner, listId, invitee.Email);

        sent.StatusCode.ShouldBe(HttpStatusCode.OK);

        var waiting = await Pending(invitee.Client);

        waiting.ShouldHaveSingleItem();
        waiting[0].ListName.ShouldBe("Oscar Winners");
        waiting[0].InvitedByEmail.ShouldBe(owner.Email);

        // Invited, not yet a member.
        (await MyLists(invitee.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_invitation_sends_the_invitee_an_email()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        await Invite(owner, listId, invitee.Email);

        var email = factory.Emails.LastTo(invitee.Email);

        email.ShouldNotBeNull();
        email.Subject.ShouldContain("Oscar Winners");
        email.HtmlBody.ShouldContain(owner.Email);
    }

    [Fact]
    public async Task An_unregistered_address_and_one_already_invited_answer_identically()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await Invite(owner, listId, invitee.Email);

        var nobody = await Invite(owner, listId, $"{Guid.NewGuid():N}@example.com");
        var alreadyInvited = await Invite(owner, listId, invitee.Email);

        // Byte for byte, not merely both-a-failure. Any difference at all —
        // status, code, wording — is enough to sort addresses into registered
        // and not.
        nobody.StatusCode.ShouldBe(alreadyInvited.StatusCode);
        (await nobody.Content.ReadAsStringAsync())
            .ShouldBe(await alreadyInvited.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unregistered_address_and_an_existing_member_answer_identically()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await JoinAsync(listId, member.Id);

        var nobody = await Invite(owner, listId, $"{Guid.NewGuid():N}@example.com");
        var alreadyIn = await Invite(owner, listId, member.Email);

        nobody.StatusCode.ShouldBe(alreadyIn.StatusCode);
        (await nobody.Content.ReadAsStringAsync())
            .ShouldBe(await alreadyIn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Inviting_yourself_is_allowed_to_be_its_own_answer()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        var response = await Invite(owner, listId, owner.Email);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Distinguishable on purpose. It only ever fires for the caller's own
        // address, so it reveals nothing about anybody else.
        (await response.Content.ReadAsStringAsync()).ShouldContain("cannot_invite_self");
    }

    [Fact]
    public async Task Somebody_outside_the_list_cannot_invite_to_it()
    {
        var owner = await factory.SignedInAsync();
        var stranger = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");

        var response = await Invite(stranger, listId, invitee.Email);

        // Checked before the address is looked at, so no address was involved
        // in the answer — and 404 makes it the same answer as for a list that
        // does not exist.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Pending(invitee.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Accepting_an_invitation_makes_you_a_member()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await Invite(owner, listId, invitee.Email);
        var membershipId = (await Pending(invitee.Client))[0].MembershipId;

        var response = await Respond(invitee, membershipId, accept: true);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MyLists(invitee.Client)).Select(x => x.Name).ShouldBe(["Oscar Winners"]);
        (await Pending(invitee.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Declining_leaves_you_out_of_the_list()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await Invite(owner, listId, invitee.Email);
        var membershipId = (await Pending(invitee.Client))[0].MembershipId;

        await Respond(invitee, membershipId, accept: false);

        (await MyLists(invitee.Client)).ShouldBeEmpty();
        (await Pending(invitee.Client)).ShouldBeEmpty();
        (await invitee.Client.GetAsync($"/lists/{listId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_invitation_can_only_be_answered_by_the_person_it_is_for()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await JoinAsync(listId, member.Id);
        await Invite(owner, listId, invitee.Email);
        var membershipId = (await Pending(invitee.Client))[0].MembershipId;

        // A member can see the whole roster, including who has not replied. It
        // does not follow that they may reply for them.
        (await Respond(member, membershipId, accept: true))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Respond(owner, membershipId, accept: true))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Pending(invitee.Client)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_answered_invitation_cannot_be_answered_again()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await Invite(owner, listId, invitee.Email);
        var membershipId = (await Pending(invitee.Client))[0].MembershipId;

        await Respond(invitee, membershipId, accept: true);

        // Accepted is terminal: leaving is deleting the membership, not
        // answering again with a different word.
        (await Respond(invitee, membershipId, accept: false))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await MyLists(invitee.Client)).Length.ShouldBe(1);
    }

    [Fact]
    public async Task Somebody_who_declined_can_be_asked_again()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await Invite(owner, listId, invitee.Email);
        await Respond(invitee, (await Pending(invitee.Client))[0].MembershipId, accept: false);

        // The Supabase trigger refused this transition, which left the
        // re-invitation branch of invite_to_list unreachable although it was
        // plainly meant to work.
        var again = await Invite(owner, listId, invitee.Email);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Pending(invitee.Client)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_code_makes_you_a_member_on_the_spot()
    {
        var owner = await factory.SignedInAsync();
        var joiner = await factory.SignedInAsync();
        var code = await JoinCodeAsync(owner, await CreateListAsync(owner, "Oscar Winners"));

        var response = await Join(joiner, code);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // No pending step: holding the code is the authorization.
        (await MyLists(joiner.Client)).Select(x => x.Name).ShouldBe(["Oscar Winners"]);
        (await Pending(joiner.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_code_is_read_the_way_somebody_would_type_it()
    {
        var owner = await factory.SignedInAsync();
        var joiner = await factory.SignedInAsync();
        var code = await JoinCodeAsync(owner, await CreateListAsync(owner, "Oscar Winners"));

        var response = await Join(joiner, $"  {code.ToLowerInvariant()}  ");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_code_that_matches_nothing_is_refused()
    {
        var joiner = await factory.SignedInAsync();

        var response = await Join(joiner, "ZZZZZZZZ");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("invalid_code");
    }

    [Fact]
    public async Task Typing_the_code_again_changes_nothing()
    {
        var owner = await factory.SignedInAsync();
        var joiner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        var code = await JoinCodeAsync(owner, listId);

        await Join(joiner, code);
        (await Join(joiner, code)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var context = factory.CreateContext();
        (await context.ListMembers.CountAsync(x => x.ListId == listId)).ShouldBe(2);
    }

    [Fact]
    public async Task The_creator_typing_their_own_code_changes_nothing()
    {
        var owner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        var code = await JoinCodeAsync(owner, listId);

        (await Join(owner, code)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var roster = await Members(owner.Client, listId);
        roster.ShouldHaveSingleItem();
        roster[0].Role.ShouldBe("owner");
    }

    [Fact]
    public async Task A_code_gets_somebody_in_who_had_turned_the_invitation_down()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await Invite(owner, listId, invitee.Email);
        await Respond(invitee, (await Pending(invitee.Client))[0].MembershipId, accept: false);

        // Turning a refusal into a membership is refused as a transition, and
        // should be. This is not that: the authorization is the code, which
        // somebody had to hand over, so the record is a new membership rather
        // than a refusal quietly rewritten.
        var response = await Join(invitee, await JoinCodeAsync(owner, listId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MyLists(invitee.Client)).Length.ShouldBe(1);

        await using var context = factory.CreateContext();
        var membership = await context.ListMembers.SingleAsync(
            x => x.ListId == listId && x.UserId == invitee.Id);
        membership.Status.ShouldBe(MemberStatus.Accepted);

        // Nobody asked them this time.
        membership.InvitedById.ShouldBeNull();
    }

    [Fact]
    public async Task A_code_accepts_an_invitation_that_was_still_waiting()
    {
        var owner = await factory.SignedInAsync();
        var invitee = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await Invite(owner, listId, invitee.Email);

        await Join(invitee, await JoinCodeAsync(owner, listId));

        (await MyLists(invitee.Client)).Length.ShouldBe(1);
        (await Pending(invitee.Client)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_the_creator_can_replace_the_code()
    {
        var owner = await factory.SignedInAsync();
        var member = await factory.SignedInAsync();
        var outsider = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        await JoinAsync(listId, member.Id);

        (await member.Client.PostAsync($"/lists/{listId}/join-code", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await outsider.Client.PostAsync($"/lists/{listId}/join-code", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await owner.Client.PostAsync($"/lists/{listId}/join-code", content: null))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Replacing_the_code_stops_the_old_one_working()
    {
        var owner = await factory.SignedInAsync();
        var joiner = await factory.SignedInAsync();
        var listId = await CreateListAsync(owner, "Oscar Winners");
        var old = await JoinCodeAsync(owner, listId);

        var replaced = await owner.Client.PostAsync($"/lists/{listId}/join-code", content: null);
        var fresh = (await replaced.Content.ReadFromJsonAsync<CodeDto>())!.JoinCode;

        fresh.ShouldNotBe(old);

        // The point of the endpoint: cutting off everyone the old code reached.
        (await Join(joiner, old)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Join(joiner, fresh)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_is_not_optional()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/lists/invites"))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/lists/join", new { code = "ABCDEFGH" }))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static Task<HttpResponseMessage> Invite(
        MovieApiFactory.SignedInUser caller,
        Guid listId,
        string email) =>
        caller.Client.PostAsJsonAsync($"/lists/{listId}/invites", new { email });

    private static Task<HttpResponseMessage> Respond(
        MovieApiFactory.SignedInUser caller,
        Guid membershipId,
        bool accept) =>
        caller.Client.PostAsJsonAsync($"/invites/{membershipId}/response", new { accept });

    private static Task<HttpResponseMessage> Join(
        MovieApiFactory.SignedInUser caller,
        string code) =>
        caller.Client.PostAsJsonAsync("/lists/join", new { code });

    private async Task<Guid> CreateListAsync(MovieApiFactory.SignedInUser owner, string name)
    {
        var response = await owner.Client.PostAsJsonAsync("/lists", new { name });

        return (await response.Content.ReadFromJsonAsync<ListDto>())!.Id;
    }

    private static async Task<string> JoinCodeAsync(
        MovieApiFactory.SignedInUser owner,
        Guid listId) =>
        (await owner.Client.GetFromJsonAsync<ListDto>($"/lists/{listId}"))!.JoinCode!;

    /// <summary>Written directly, for the tests where getting in is not the subject.</summary>
    private async Task JoinAsync(Guid listId, Guid userId)
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

    private static async Task<InviteDto[]> Pending(HttpClient client) =>
        (await client.GetFromJsonAsync<InviteDto[]>("/lists/invites"))!;

    private static async Task<ListDto[]> MyLists(HttpClient client) =>
        (await client.GetFromJsonAsync<ListDto[]>("/lists"))!;

    private static async Task<MemberDto[]> Members(HttpClient client, Guid listId) =>
        (await client.GetFromJsonAsync<MemberDto[]>($"/lists/{listId}/members"))!;

    private sealed record ListDto(Guid Id, string Name, string? JoinCode);

    private sealed record InviteDto(
        Guid MembershipId,
        Guid ListId,
        string ListName,
        string? InvitedByEmail,
        DateTime CreatedAt);

    private sealed record MemberDto(Guid MembershipId, Guid UserId, string Role, string Status);

    private sealed record CodeDto(string JoinCode);
}