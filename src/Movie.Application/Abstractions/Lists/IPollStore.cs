using Movie.Domain.Lists;

namespace Movie.Application.Abstractions.Lists;

/// <summary>
/// "What are we watching tonight": a shortlist drawn from a list's own items,
/// a deadline, and one vote each.
/// </summary>
public interface IPollStore
{
    /// <summary>
    /// The list's most recent poll — running or just finished — or null if it
    /// has never had one.
    /// </summary>
    /// <remarks>
    /// Whether a poll is over is not stored anywhere; it is over once its
    /// deadline has passed. That is what makes a poll needing to be closed
    /// something no background job has to notice, and it is also why this
    /// returns a finished poll rather than hiding it: the results are the
    /// point of having voted.
    /// </remarks>
    Task<PollSnapshot?> LatestAsync(MediaList list, CancellationToken cancellationToken = default);

    Task<StartPollResult> StartAsync(
        MediaList list,
        DateTime deadline,
        IReadOnlyList<Guid> itemIds,
        CancellationToken cancellationToken = default);

    /// <remarks>
    /// Changing your mind rewrites the vote rather than adding one, which is
    /// what "one vote per person" means when people are allowed to reconsider.
    /// </remarks>
    Task<CastVoteOutcome> VoteAsync(
        ListPoll poll,
        Guid candidateId,
        CancellationToken cancellationToken = default);
}

/// <param name="Candidates">
/// Counted in the database rather than by loading every vote, and carrying the
/// caller's own choice so the screen can show it without a second request.
/// </param>
public sealed record PollSnapshot(
    Guid PollId,
    DateTime Deadline,
    Guid CreatedById,
    IReadOnlyList<PollCandidateTally> Candidates);

/// <param name="ListItemId">
/// A candidate points at a list item rather than copying its title, so a title
/// removed from the list stops being a candidate as well — a poll never goes on
/// offering something that is no longer there.
/// </param>
public sealed record PollCandidateTally(
    Guid CandidateId,
    Guid ListItemId,
    int VoteCount,
    bool MyVote);

public enum StartPollOutcome
{
    Started,

    /// <summary>A poll that has already finished is not worth opening.</summary>
    DeadlineNotInFuture,

    /// <summary>
    /// A vote between fewer than two things is not a vote. Counted after
    /// duplicates are dropped, since naming one title twice does not make two.
    /// </summary>
    NeedAtLeastTwoCandidates,

    /// <summary>
    /// One running poll per list. Two at once would split the members between
    /// them and settle nothing.
    /// </summary>
    PollAlreadyActive,

    /// <summary>
    /// One of the nominated items is not on this list.
    /// </summary>
    /// <remarks>
    /// The Supabase function did not check this: its foreign key established
    /// only that the item existed <em>somewhere</em>, so anyone in two lists
    /// could nominate one list's item into the other's poll.
    /// </remarks>
    UnknownCandidates,
}

public sealed record StartPollResult(StartPollOutcome Outcome, Guid? PollId);

public enum CastVoteOutcome
{
    Recorded,

    /// <summary>
    /// The deadline has passed. Checked as the vote is cast, which is the only
    /// moment it needs to be checked at.
    /// </summary>
    PollClosed,

    /// <summary>
    /// The candidate belongs to another poll, or to nothing. Resolved through
    /// the poll rather than trusting the pairing the caller sent.
    /// </summary>
    InvalidCandidate,
}
