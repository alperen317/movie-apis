using Movie.Domain.Users;

namespace Movie.Domain.Lists;

/// <summary>
/// One member's vote in one poll. One vote per person per poll — a real
/// election, not per-item upvotes.
/// </summary>
public sealed class ListPollVote
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>
    /// Technically derivable, since the candidate already belongs to a poll. It
    /// is stored anyway because the "one vote per person per poll" rule is
    /// enforced by a uniqueness constraint over <c>PollId</c> + <c>UserId</c>,
    /// which requires the column to physically exist on the table.
    /// </summary>
    public required Guid PollId { get; init; }

    public ListPoll? Poll { get; init; }

    /// <summary><c>set</c> because a member may change their vote.</summary>
    public required Guid CandidateId { get; set; }

    public ListPollCandidate? Candidate { get; set; }

    public required Guid UserId { get; init; }

    public ApplicationUser? User { get; init; }

    /// <summary>Refreshed when the vote changes.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}