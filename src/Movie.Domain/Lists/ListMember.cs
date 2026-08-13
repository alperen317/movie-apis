using Movie.Domain.Users;

namespace Movie.Domain.Lists;

/// <summary>
/// One user's relationship to one list. The same row represents an accepted
/// membership, a pending invitation and a declined one — there is no separate
/// invitations table, because an invite always targets an existing account.
/// </summary>
public sealed class ListMember
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid ListId { get; init; }

    public MediaList? List { get; init; }

    public required Guid UserId { get; init; }

    /// <summary>Source of the member's name and avatar on the roster screen.</summary>
    public ApplicationUser? User { get; init; }

    /// <summary>Never changes: there is no transfer or promotion flow.</summary>
    public MemberRole Role { get; init; } = MemberRole.Member;

    public MemberStatus Status { get; set; } = MemberStatus.Pending;

    /// <summary>
    /// Who sent the invitation. <c>set</c> because a declined member can be
    /// re-invited, possibly by someone else.
    /// </summary>
    public Guid? InvitedById { get; set; }

    public ApplicationUser? InvitedBy { get; set; }

    /// <summary>
    /// When the invitation was sent. Reset on re-invitation, since the pending
    /// invites list is ordered by it and a fresh invite belongs at the top.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null until the invitation is answered.</summary>
    public DateTime? RespondedAt { get; set; }
}
