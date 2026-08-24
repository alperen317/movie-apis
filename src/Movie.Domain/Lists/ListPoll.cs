using Movie.Domain.Users;

namespace Movie.Domain.Lists;

/// <summary>
/// A "what should we watch tonight" poll: a shortlist of candidates drawn from
/// the list's own items, plus a deadline. Each member picks <em>one</em> of the
/// candidates.
/// </summary>
/// <remarks>
/// Closed-ness is not stored: a poll is closed once <see cref="Deadline"/> has
/// passed. That removes the need for any background job to close polls —
/// closure is checked when a vote is cast.
/// </remarks>
public sealed class ListPoll
{
    /// <summary>Minimum number of candidates required to open a poll.</summary>
    public const int MinimumCandidates = 2;

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid ListId { get; init; }

    public MediaList? List { get; init; }

    public required Guid CreatedById { get; init; }

    public ApplicationUser? CreatedBy { get; init; }

    public required DateTime Deadline { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<ListPollCandidate> Candidates { get; } = [];

    /// <summary>
    /// A method rather than a property: "now" is supplied by the caller, which
    /// keeps it testable and stops EF from mistaking it for a mapped column.
    /// </summary>
    public bool IsOpenAt(DateTime utcNow) => utcNow <= Deadline;
}