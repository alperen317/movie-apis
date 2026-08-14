using Movie.Domain.Lists;

namespace Movie.Application.Abstractions.Lists;

/// <summary>
/// Getting into a list: by invitation, or by knowing its code.
/// </summary>
/// <remarks>
/// <para>
/// Looking an account up by address happens inside <see cref="InviteAsync"/>
/// and nowhere else, and its answer never leaves. Supabase had that lookup as
/// an RPC of its own, granted to every signed-in user, which made it an
/// unthrottled and perfectly clear "is this address registered?" oracle —
/// worse than the side channel in <c>invite_to_list</c> that was carefully
/// closed separately. There is no method here that answers that question, and
/// that is the point of there not being one.
/// </para>
/// </remarks>
public interface IInvitationStore
{
    /// <summary>
    /// Invites an address to a list the caller has already been shown to be a
    /// member of.
    /// </summary>
    /// <remarks>
    /// <see cref="InviteOutcome.Failed"/> covers an address with no account and
    /// an address already invited or already a member, deliberately without
    /// distinguishing them. Telling them apart let the owner of a list they
    /// control invite arbitrary addresses and read which answer came back, one
    /// at a time, to learn who has an account.
    /// </remarks>
    Task<InviteResult> InviteAsync(
        MediaList list,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invitations waiting for the caller to answer, newest first.
    /// </summary>
    /// <remarks>
    /// Only the caller's own. An accepted member can see the whole roster of a
    /// list including who has not replied yet, so a query that did not say
    /// whose invitations these are would sweep in other people's.
    /// </remarks>
    Task<IReadOnlyList<ListMember>> PendingForMeAsync(CancellationToken cancellationToken = default);

    Task RespondAsync(
        ListMember invitation,
        bool accept,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Joins the list a code names, and returns it. Null when no list has that
    /// code.
    /// </summary>
    /// <remarks>
    /// There is no pending step: holding the code is the authorization. Joining
    /// twice is a no-op rather than an error.
    /// </remarks>
    Task<MediaList?> JoinByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a list's code, which is how the owner cuts off everyone who was
    /// given the old one.
    /// </summary>
    Task<string> RegenerateCodeAsync(
        MediaList list,
        CancellationToken cancellationToken = default);
}

public enum InviteOutcome
{
    Invited,

    /// <summary>
    /// No account, or already invited, or already a member. One answer on
    /// purpose.
    /// </summary>
    Failed,

    /// <summary>
    /// Stays separate from <see cref="Failed"/> because it only ever fires for
    /// the caller's own address, so it tells them nothing they did not already
    /// know about anybody.
    /// </summary>
    CannotInviteSelf,
}

/// <param name="Membership">Set only when the invitation was actually sent.</param>
public sealed record InviteResult(InviteOutcome Outcome, ListMember? Membership);
