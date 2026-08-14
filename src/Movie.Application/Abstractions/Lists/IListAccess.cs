using Movie.Domain.Lists;

namespace Movie.Application.Abstractions.Lists;

/// <summary>
/// The only way a handler reaches a shared list.
/// </summary>
/// <remarks>
/// <para>
/// Supabase enforced this with row-level security, so no query could reach a
/// list the caller had no business seeing. Handlers here do not query
/// <c>lists</c> at all; they ask for the access they need and get null when
/// they do not have it. Forgetting a check is not possible, because there is no
/// unguarded path to forget it on.
/// </para>
/// <para>
/// The methods differ because the rules did. Reading a list's items requires an
/// accepted membership, while merely seeing its name is open to someone still
/// deciding on an invitation, and deleting it is the creator's alone.
/// </para>
/// <para>
/// Every method resolves the caller from the request rather than taking a user
/// id, which removes the possibility of checking the wrong person.
/// </para>
/// </remarks>
public interface IListAccess
{
    /// <summary>
    /// For anything that touches a list's contents. Requires an accepted
    /// membership; a pending invitee is not a member yet.
    /// </summary>
    Task<MediaList?> ForMemberAsync(Guid listId, CancellationToken cancellationToken = default);

    /// <summary>
    /// For renaming's stricter cousins — deleting the list, removing other
    /// people, regenerating the join code.
    /// </summary>
    Task<MediaList?> ForOwnerAsync(Guid listId, CancellationToken cancellationToken = default);

    /// <summary>
    /// For the invitation card, which shows a list's name to someone who has
    /// not accepted yet. Accepted members pass this too.
    /// </summary>
    Task<MediaList?> ForViewerAsync(Guid listId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls and their candidates carry no list id of their own, so membership
    /// is resolved through the poll rather than trusting one supplied by the
    /// caller.
    /// </summary>
    Task<ListPoll?> PollForMemberAsync(Guid pollId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the caller may see another person's profile, which they may only
    /// if the two share a list.
    /// </summary>
    Task<bool> SharesAListWithAsync(Guid otherUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A membership the caller is allowed to delete, or null when there is no
    /// such membership of theirs to reach.
    /// </summary>
    /// <remarks>
    /// One method rather than two, because removing somebody and leaving of
    /// your own accord are the same delete: the list's creator may remove
    /// anyone, and anyone may remove themselves. Keeping both halves of that
    /// rule in one place is what stops them drifting apart.
    /// </remarks>
    Task<ListMember?> MembershipToRemoveAsync(
        Guid membershipId,
        CancellationToken cancellationToken = default);
}
