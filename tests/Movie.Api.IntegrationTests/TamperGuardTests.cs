using Microsoft.EntityFrameworkCore;
using Movie.Domain.Lists;
using Movie.Domain.Users;
using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// The two triggers that guarded columns the row policies could not. Each test
/// makes the write the trigger existed to stop.
/// </summary>
public sealed class TamperGuardTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_lists_creator_cannot_be_rewritten()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var listId = await CreateListAsync(owner, (member, MemberStatus.Accepted));

        await using var context = postgres.CreateContext();
        var list = await context.Lists.SingleAsync(x => x.Id == listId);
        list.Name = "Renamed";
        context.Entry(list).Property(x => x.CreatedById).CurrentValue = member;

        // Renaming is allowed for any member, and the row policy could not tell
        // a rename from a rename that also hands over the list.
        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task Renaming_on_its_own_still_works()
    {
        var owner = await CreateUserAsync();
        var listId = await CreateListAsync(owner);

        await using var context = postgres.CreateContext();
        var list = await context.Lists.SingleAsync(x => x.Id == listId);
        list.Name = "Renamed";
        await context.SaveChangesAsync();

        (await context.Lists.SingleAsync(x => x.Id == listId)).Name.ShouldBe("Renamed");
    }

    [Fact]
    public async Task A_membership_cannot_be_repointed_at_another_list()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var mine = await CreateListAsync(owner, (member, MemberStatus.Accepted));
        var somebodyElses = await CreateListAsync(await CreateUserAsync());

        await using var context = postgres.CreateContext();
        var membership = await context.ListMembers.SingleAsync(
            x => x.ListId == mine && x.UserId == member);
        context.Entry(membership).Property(x => x.ListId).CurrentValue = somebodyElses;

        // This is the attack the trigger existed for: take a row you are
        // allowed to update and aim it at a list you were never invited to.
        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_member_cannot_promote_themselves()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var listId = await CreateListAsync(owner, (member, MemberStatus.Accepted));

        await using var context = postgres.CreateContext();
        var membership = await context.ListMembers.SingleAsync(
            x => x.ListId == listId && x.UserId == member);
        context.Entry(membership).Property(x => x.Role).CurrentValue = MemberRole.Owner;

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_invitation_can_be_accepted()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var listId = await CreateListAsync(owner, (invitee, MemberStatus.Pending));

        await using var context = postgres.CreateContext();
        var membership = await context.ListMembers.SingleAsync(
            x => x.ListId == listId && x.UserId == invitee);
        membership.Status = MemberStatus.Accepted;
        membership.RespondedAt = DateTime.UtcNow;

        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task An_accepted_membership_cannot_be_walked_back()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var listId = await CreateListAsync(owner, (member, MemberStatus.Accepted));

        await using var context = postgres.CreateContext();
        var membership = await context.ListMembers.SingleAsync(
            x => x.ListId == listId && x.UserId == member);
        membership.Status = MemberStatus.Pending;

        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_declined_invitation_can_be_sent_again()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var listId = await CreateListAsync(owner, (invitee, MemberStatus.Declined));

        await using var context = postgres.CreateContext();
        var membership = await context.ListMembers.SingleAsync(
            x => x.ListId == listId && x.UserId == invitee);
        membership.Status = MemberStatus.Pending;
        membership.CreatedAt = DateTime.UtcNow;
        membership.RespondedAt = null;

        // The Supabase trigger refused this, which left the re-invitation path
        // in invite_to_list unreachable.
        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task A_declined_invitation_cannot_be_self_accepted()
    {
        var owner = await CreateUserAsync();
        var invitee = await CreateUserAsync();
        var listId = await CreateListAsync(owner, (invitee, MemberStatus.Declined));

        await using var context = postgres.CreateContext();
        var membership = await context.ListMembers.SingleAsync(
            x => x.ListId == listId && x.UserId == invitee);
        membership.Status = MemberStatus.Accepted;

        // Turning an invitation down and then letting yourself in would be a
        // membership nobody offered.
        await Should.ThrowAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

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
