using Microsoft.EntityFrameworkCore;
using Movie.Domain.Lists;
using Movie.Domain.Users;
using Movie.Infrastructure.Authentication;
using Movie.Infrastructure.Lists;
using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// The replacement for the SECURITY DEFINER helpers that backed the shared-list
/// policies. Each test states who is asking and what they may reach.
/// </summary>
public sealed class ListAccessTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task An_accepted_member_reaches_the_list()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var list = await CreateListAsync(owner, (member, MemberStatus.Accepted));

        (await Access(member).ForMemberAsync(list)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Someone_with_no_membership_reaches_nothing()
    {
        var owner = await CreateUserAsync();
        var stranger = await CreateUserAsync();
        var list = await CreateListAsync(owner);

        (await Access(stranger).ForMemberAsync(list)).ShouldBeNull();
        (await Access(stranger).ForViewerAsync(list)).ShouldBeNull();
        (await Access(stranger).ForOwnerAsync(list)).ShouldBeNull();
    }

    [Fact]
    public async Task A_pending_invitee_sees_the_list_but_not_its_contents()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var list = await CreateListAsync(owner, (invitee, MemberStatus.Pending));

        // Enough to render "Alice invited you to Oscar Winners"…
        (await Access(invitee).ForViewerAsync(list)).ShouldNotBeNull();

        // …and not enough to read what is in it.
        (await Access(invitee).ForMemberAsync(list)).ShouldBeNull();
    }

    [Fact]
    public async Task A_declined_invitation_grants_nothing()
    {
        var owner = await CreateUserAsync();
        var declined = await CreateUserAsync();
        var list = await CreateListAsync(owner, (declined, MemberStatus.Declined));

        (await Access(declined).ForViewerAsync(list)).ShouldBeNull();
        (await Access(declined).ForMemberAsync(list)).ShouldBeNull();
    }

    [Fact]
    public async Task Ownership_follows_the_list_not_the_membership_row()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var list = await CreateListAsync(owner, (member, MemberStatus.Accepted));

        // Written in SQL on purpose. Going through the context is refused by
        // the tamper guard, so this puts the database in the state that guard
        // exists to prevent and checks the answer does not depend on it.
        await using (var context = postgres.CreateContext())
        {
            await context.Database.ExecuteSqlAsync(
                $"update list_members set role = 'owner' where user_id = {member} and list_id = {list}");
        }

        (await Access(member).ForOwnerAsync(list)).ShouldBeNull();
        (await Access(owner).ForOwnerAsync(list)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_poll_is_reached_through_its_own_list_not_a_supplied_one()
    {
        var owner = await CreateUserAsync();
        var stranger = await CreateUserAsync();
        var list = await CreateListAsync(owner);
        var poll = await CreatePollAsync(list, owner);

        (await Access(owner).PollForMemberAsync(poll)).ShouldNotBeNull();
        (await Access(stranger).PollForMemberAsync(poll)).ShouldBeNull();
    }

    [Fact]
    public async Task Profiles_are_visible_between_people_who_share_a_list()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var stranger = await CreateUserAsync();
        await CreateListAsync(owner, (member, MemberStatus.Accepted));

        (await Access(member).SharesAListWithAsync(owner)).ShouldBeTrue();
        (await Access(owner).SharesAListWithAsync(member)).ShouldBeTrue();
        (await Access(stranger).SharesAListWithAsync(owner)).ShouldBeFalse();
    }

    [Fact]
    public async Task Two_people_still_deciding_cannot_see_each_other()
    {
        var owner = await CreateUserAsync();
        var first = await CreateUserAsync();
        var second = await CreateUserAsync();
        await CreateListAsync(owner, (first, MemberStatus.Pending), (second, MemberStatus.Pending));

        // Being invited alongside someone is not a relationship. The person
        // being looked at has to have joined.
        (await Access(first).SharesAListWithAsync(second)).ShouldBeFalse();

        // The one who created the list has, so they are visible to an invitee
        // deciding whether to accept.
        (await Access(first).SharesAListWithAsync(owner)).ShouldBeTrue();
    }

    [Fact]
    public async Task Nobody_signed_in_reaches_nothing()
    {
        var owner = await CreateUserAsync();
        var list = await CreateListAsync(owner);

        var anonymous = new ListAccess(postgres.CreateContext(), StaticCurrentUser.Anonymous);

        (await anonymous.ForMemberAsync(list)).ShouldBeNull();
        (await anonymous.ForViewerAsync(list)).ShouldBeNull();
        (await anonymous.ForOwnerAsync(list)).ShouldBeNull();
        (await anonymous.SharesAListWithAsync(owner)).ShouldBeFalse();
    }

    private ListAccess Access(Guid userId) =>
        new(postgres.CreateContext(userId), new StaticCurrentUser(userId));

    private async Task<Guid> CreateListAsync(
        Guid ownerId,
        params (Guid UserId, MemberStatus Status)[] others)
    {
        await using var context = postgres.CreateContext();

        var list = new MediaList { Name = "Oscar Winners", CreatedById = ownerId };
        context.Lists.Add(list);

        context.ListMembers.Add(new ListMember
        {
            ListId = list.Id,
            UserId = ownerId,
            Role = MemberRole.Owner,
            Status = MemberStatus.Accepted,
            RespondedAt = DateTime.UtcNow,
        });

        foreach (var (userId, status) in others)
        {
            context.ListMembers.Add(new ListMember
            {
                ListId = list.Id,
                UserId = userId,
                Status = status,
                InvitedById = ownerId,
            });
        }

        await context.SaveChangesAsync();

        return list.Id;
    }

    private async Task<Guid> CreatePollAsync(Guid listId, Guid createdById)
    {
        await using var context = postgres.CreateContext();

        var poll = new ListPoll
        {
            ListId = listId,
            CreatedById = createdById,
            Deadline = DateTime.UtcNow.AddDays(1),
        };

        context.ListPolls.Add(poll);
        await context.SaveChangesAsync();

        return poll.Id;
    }

    private async Task<Guid> CreateUserAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
        };
        user.UserName = user.Email;
        user.NormalizedEmail = user.Email!.ToUpperInvariant();
        user.NormalizedUserName = user.NormalizedEmail;
        user.SecurityStamp = Guid.NewGuid().ToString();

        await using var context = postgres.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }
}
