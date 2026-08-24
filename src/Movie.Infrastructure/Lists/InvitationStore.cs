using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;
using Movie.Domain.Users;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Lists;

/// <inheritdoc cref="IInvitationStore"/>
public sealed class InvitationStore(
    MovieDbContext database,
    UserManager<ApplicationUser> users,
    ICurrentUser currentUser) : IInvitationStore
{
    private const int CodeAttempts = 3;

    public async Task<InviteResult> InviteAsync(
        MediaList list,
        string email,
        CancellationToken cancellationToken = default)
    {
        var inviterId = currentUser.Id
            ?? throw new InvalidOperationException("Nobody cannot send an invitation.");

        // Through UserManager so the address is normalised the same way it was
        // when the account was created. This answer does not leave the method.
        var invitee = await users.FindByEmailAsync(email);

        if (invitee is not null && invitee.Id == inviterId)
        {
            return new InviteResult(InviteOutcome.CannotInviteSelf, null);
        }

        if (invitee is null)
        {
            // Indistinguishable from "already invited" below, which is the
            // whole point — see IInvitationStore.InviteAsync.
            return new InviteResult(InviteOutcome.Failed, null);
        }

        var existing = await database.ListMembers.FirstOrDefaultAsync(
            membership => membership.ListId == list.Id && membership.UserId == invitee.Id,
            cancellationToken);

        if (existing is null)
        {
            var membership = new ListMember
            {
                ListId = list.Id,
                UserId = invitee.Id,
                Status = MemberStatus.Pending,
                InvitedById = inviterId,
            };

            database.ListMembers.Add(membership);
            await database.SaveChangesAsync(cancellationToken);

            await WithProfileAsync(membership, cancellationToken);

            return new InviteResult(InviteOutcome.Invited, membership);
        }

        if (existing.Status is not MemberStatus.Declined)
        {
            // Pending or accepted: nothing to send. Same answer as no account.
            return new InviteResult(InviteOutcome.Failed, null);
        }

        // Somebody who said no can be asked again. The Supabase trigger refused
        // this transition, which left the re-invitation branch of
        // invite_to_list unreachable although it was plainly meant to work;
        // phase 3 opened it deliberately.
        existing.Status = MemberStatus.Pending;
        existing.InvitedById = inviterId;
        existing.RespondedAt = null;

        // Reset because the pending list is ordered by it, and an invitation
        // sent today belongs at the top rather than where the refused one sat.
        existing.CreatedAt = DateTime.UtcNow;

        await database.SaveChangesAsync(cancellationToken);

        await WithProfileAsync(existing, cancellationToken);

        return new InviteResult(InviteOutcome.Invited, existing);
    }

    /// <summary>
    /// Fills in the invited person's profile — the roster entry the caller
    /// gets back is drawn from it — and the inviter's, which the invite email
    /// needs for its "so-and-so invited you" line.
    /// </summary>
    private async Task WithProfileAsync(ListMember membership, CancellationToken cancellationToken)
    {
        await database.Entry(membership).Reference(x => x.User).LoadAsync(cancellationToken);
        await database.Entry(membership).Reference(x => x.InvitedBy).LoadAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ListMember>> PendingForMeAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return [];
        }

        return await database.ListMembers
            .Where(membership => membership.UserId == userId
                && membership.Status == MemberStatus.Pending)
            .Include(membership => membership.List)
            .Include(membership => membership.InvitedBy)
            .OrderByDescending(membership => membership.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task RespondAsync(
        ListMember invitation,
        bool accept,
        CancellationToken cancellationToken = default)
    {
        // Whether this transition is allowed at all is settled in SaveChanges,
        // for the reason the original was a trigger: a rule that only holds
        // where somebody remembered to write it does not hold.
        invitation.Status = accept ? MemberStatus.Accepted : MemberStatus.Declined;
        invitation.RespondedAt = DateTime.UtcNow;

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<MediaList?> JoinByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id
            ?? throw new InvalidOperationException("Nobody cannot join a list.");

        // Upper-cased to match how the alphabet is written, so a code typed in
        // lower case still finds its list.
        var normalized = code.Trim().ToUpperInvariant();

        var list = await database.Lists.FirstOrDefaultAsync(
            candidate => candidate.JoinCode == normalized,
            cancellationToken);

        if (list is null)
        {
            return null;
        }

        var existing = await database.ListMembers.FirstOrDefaultAsync(
            membership => membership.ListId == list.Id && membership.UserId == userId,
            cancellationToken);

        switch (existing)
        {
            case { Status: MemberStatus.Accepted }:
                // Already in. Typing the code again changes nothing.
                return list;

            case { Status: MemberStatus.Pending }:
                existing.Status = MemberStatus.Accepted;
                existing.RespondedAt = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                return list;

            case { Status: MemberStatus.Declined }:
                await ReplaceDeclinedAsync(existing, list, userId, cancellationToken);
                return list;

            default:
                database.ListMembers.Add(NewMembership(list, userId));
                await database.SaveChangesAsync(cancellationToken);
                return list;
        }
    }

    public async Task<string> RegenerateCodeAsync(
        MediaList list,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            list.JoinCode = JoinCodeGenerator.Generate();

            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return list.JoinCode;
            }
            catch (DbUpdateException e)
                when (attempt < CodeAttempts && UniqueViolations.Caused(e))
            {
                // Practically unreachable with 32^8 codes. Here so that if it
                // ever happens it costs a round trip rather than a failure.
            }
        }
    }

    /// <summary>
    /// Turns a declined invitation into a fresh membership by replacing the
    /// row rather than moving it forward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declined to accepted is refused outright by the transition rule, and
    /// should be: letting somebody flip their own refusal into a membership is
    /// exactly the tampering that rule exists to stop.
    /// </para>
    /// <para>
    /// Joining by code is not that. It is not an answer to the old invitation
    /// at all — the authorization is the code, which the caller had to be given
    /// — so the honest record is a new membership, not a refusal quietly
    /// rewritten. Deleting first also means the rule stays as strict as it was.
    /// </para>
    /// <para>
    /// Two saves in one transaction, because a delete and an insert of the same
    /// (list, user) pair in a single batch would race the unique index.
    /// </para>
    /// </remarks>
    private async Task ReplaceDeclinedAsync(
        ListMember declined,
        MediaList list,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken);

        database.ListMembers.Remove(declined);
        await database.SaveChangesAsync(cancellationToken);

        database.ListMembers.Add(NewMembership(list, userId));
        await database.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    /// <remarks>
    /// Accepted straight away, with no inviter: nobody asked this person, they
    /// let themselves in with a code somebody gave them.
    /// </remarks>
    private static ListMember NewMembership(MediaList list, Guid userId) => new()
    {
        ListId = list.Id,
        UserId = userId,
        Status = MemberStatus.Accepted,
        RespondedAt = DateTime.UtcNow,
    };
}
