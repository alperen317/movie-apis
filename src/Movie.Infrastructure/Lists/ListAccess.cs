using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Lists;

public sealed class ListAccess(MovieDbContext database, ICurrentUser currentUser) : IListAccess
{
    public Task<MediaList?> ForMemberAsync(Guid listId, CancellationToken cancellationToken = default) =>
        FindAsync(listId, MembershipRequirement.Accepted, cancellationToken);

    public Task<MediaList?> ForViewerAsync(Guid listId, CancellationToken cancellationToken = default) =>
        FindAsync(listId, MembershipRequirement.AcceptedOrPending, cancellationToken);

    public async Task<MediaList?> ForOwnerAsync(
        Guid listId,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return null;
        }

        // Ownership is read off the list itself rather than the member row's
        // role, so a tampered membership cannot confer it.
        return await database.Lists.FirstOrDefaultAsync(
            list => list.Id == listId && list.CreatedById == userId,
            cancellationToken);
    }

    public async Task<ListPoll?> PollForMemberAsync(
        Guid pollId,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return null;
        }

        return await database.ListPolls.FirstOrDefaultAsync(
            poll => poll.Id == pollId
                && database.ListMembers.Any(member =>
                    member.ListId == poll.ListId
                    && member.UserId == userId
                    && member.Status == MemberStatus.Accepted),
            cancellationToken);
    }

    public async Task<bool> SharesAListWithAsync(
        Guid otherUserId,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId || userId == otherUserId)
        {
            return false;
        }

        // The caller may still be deciding on their invitation, but the person
        // whose profile they are looking at must have joined. Otherwise two
        // people invited to the same list could see each other before either
        // had anything to do with it.
        return await database.ListMembers.AnyAsync(
            mine => mine.UserId == userId
                && (mine.Status == MemberStatus.Accepted || mine.Status == MemberStatus.Pending)
                && database.ListMembers.Any(theirs =>
                    theirs.ListId == mine.ListId
                    && theirs.UserId == otherUserId
                    && theirs.Status == MemberStatus.Accepted),
            cancellationToken);
    }

    public async Task<ListMember?> MembershipToRemoveAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return null;
        }

        // The list comes along because the caller of this needs to know whether
        // the membership being removed is the creator's own.
        return await database.ListMembers
            .Include(membership => membership.List)
            .FirstOrDefaultAsync(
                membership => membership.Id == membershipId
                    && (membership.UserId == userId
                        || membership.List!.CreatedById == userId),
                cancellationToken);
    }

    private async Task<MediaList?> FindAsync(
        Guid listId,
        MembershipRequirement requirement,
        CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } userId)
        {
            return null;
        }

        return await database.Lists.FirstOrDefaultAsync(
            list => list.Id == listId
                && database.ListMembers.Any(member =>
                    member.ListId == list.Id
                    && member.UserId == userId
                    && (member.Status == MemberStatus.Accepted
                        || (requirement == MembershipRequirement.AcceptedOrPending
                            && member.Status == MemberStatus.Pending))),
            cancellationToken);
    }

    private enum MembershipRequirement
    {
        Accepted,
        AcceptedOrPending,
    }
}
