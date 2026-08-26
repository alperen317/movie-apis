using Mediator;

using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;
using Movie.Domain.Users;

namespace Movie.Application.Features.Lists;

/// <summary>
/// The roster of one list.
/// </summary>
public sealed record GetListMembersQuery(Guid ListId) : IRequest<IReadOnlyList<ListMemberDto>?>;

/// <param name="Email">
/// Visible to co-members on purpose. This is the roster of a list they are all
/// in, which is the relationship that makes a profile visible at all — see
/// <see cref="IListAccess.SharesAListWithAsync"/>.
/// </param>
/// <param name="Status">
/// Invitations that have not been answered are on the roster too, so the
/// members screen can show who has yet to reply.
/// </param>
public sealed record ListMemberDto(
    Guid MembershipId,
    Guid ListId,
    Guid UserId,
    string Email,
    string? DisplayName,
    AvatarVariant AvatarVariant,
    string? AvatarSeed,
    MemberRole Role,
    MemberStatus Status,
    Guid? InvitedBy,
    DateTime CreatedAt,
    DateTime? RespondedAt)
{
    public static ListMemberDto From(ListMember membership) => new(
        membership.Id,
        membership.ListId,
        membership.UserId,
        membership.User?.Email ?? string.Empty,
        membership.User?.DisplayName,
        membership.User?.AvatarVariant ?? AvatarVariant.Beam,
        membership.User?.AvatarSeed,
        membership.Role,
        membership.Status,
        membership.InvitedById,
        membership.CreatedAt,
        membership.RespondedAt);
}

public sealed class GetListMembersQueryHandler(IListAccess access, IListStore lists)
    : IRequestHandler<GetListMembersQuery, IReadOnlyList<ListMemberDto>?>
{
    /// <returns>Null when the caller is not a member of the list.</returns>
    public async ValueTask<IReadOnlyList<ListMemberDto>?> Handle(
        GetListMembersQuery query,
        CancellationToken cancellationToken)
    {
        // A pending invitee deliberately does not pass this. Seeing a list's
        // name is one thing; reading off everyone in it before deciding to
        // join is another.
        var list = await access.ForMemberAsync(query.ListId, cancellationToken);

        if (list is null)
        {
            return null;
        }

        var members = await lists.MembersAsync(list, cancellationToken);

        return [.. members.Select(ListMemberDto.From)];
    }
}

/// <summary>
/// Removes somebody from a list, or leaves one.
/// </summary>
/// <remarks>
/// The same command either way, because it was the same delete in Supabase and
/// the difference is only who the row belongs to.
/// </remarks>
public sealed record RemoveMemberCommand(Guid MembershipId) : IRequest<RemoveMemberOutcome>;

public enum RemoveMemberOutcome
{
    Removed,

    /// <summary>
    /// No such membership, or none this caller may touch. The two are one
    /// answer on purpose — telling them apart would confirm a membership
    /// exists to somebody with no business knowing.
    /// </summary>
    NotFound,

    /// <summary>
    /// The creator tried to leave their own list.
    /// </summary>
    CreatorCannotLeave,
}

public sealed class RemoveMemberCommandHandler(
    IListAccess access,
    IListStore lists,
    IListEventPublisher events)
    : IRequestHandler<RemoveMemberCommand, RemoveMemberOutcome>
{
    public async ValueTask<RemoveMemberOutcome> Handle(
        RemoveMemberCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await access.MembershipToRemoveAsync(
            command.MembershipId,
            cancellationToken);

        if (membership is null)
        {
            return RemoveMemberOutcome.NotFound;
        }

        // Refused because ownership is read off the list's creator column, not
        // off a membership row: the creator who left would still be the only
        // one able to delete the list, while no longer being able to read it.
        // Deleting the list is how you get out of one you started.
        if (membership.UserId == membership.List?.CreatedById)
        {
            return RemoveMemberOutcome.CreatorCannotLeave;
        }

        await lists.RemoveMemberAsync(membership, cancellationToken);

        await events.MembersChangedAsync(membership.ListId, cancellationToken);
        await events.MemberEvictedAsync(membership.ListId, membership.UserId, cancellationToken);

        return RemoveMemberOutcome.Removed;
    }
}