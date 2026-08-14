using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Lists;

/// <inheritdoc cref="IPollStore"/>
public sealed class PollStore(MovieDbContext database, ICurrentUser currentUser) : IPollStore
{
    public async Task<PollSnapshot?> LatestAsync(
        MediaList list,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id;

        var poll = await database.ListPolls
            .Where(candidate => candidate.ListId == list.Id)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .Select(found => new PollSnapshot(
                found.Id,
                found.Deadline,
                found.CreatedById,
                found.Candidates
                    .Select(candidate => new PollCandidateTally(
                        candidate.Id,
                        candidate.ListItemId,
                        candidate.Votes.Count,
                        candidate.Votes.Any(vote => vote.UserId == userId)))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);

        return poll;
    }

    public async Task<StartPollResult> StartAsync(
        MediaList list,
        DateTime deadline,
        IReadOnlyList<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id
            ?? throw new InvalidOperationException("Nobody cannot start a poll.");

        var now = DateTime.UtcNow;

        if (deadline <= now)
        {
            return Failed(StartPollOutcome.DeadlineNotInFuture);
        }

        // Naming the same title twice does not make it two candidates, and the
        // uniqueness on (poll, item) would refuse the second one anyway.
        var candidates = itemIds.Distinct().ToList();

        if (candidates.Count < ListPoll.MinimumCandidates)
        {
            return Failed(StartPollOutcome.NeedAtLeastTwoCandidates);
        }

        // Every candidate has to be on this list. Without this a member of two
        // lists could nominate one list's title into the other's poll, which
        // the foreign key alone does not prevent — it only says the item exists.
        var onThisList = await database.ListItems
            .Where(item => item.ListId == list.Id && candidates.Contains(item.Id))
            .CountAsync(cancellationToken);

        if (onThisList != candidates.Count)
        {
            return Failed(StartPollOutcome.UnknownCandidates);
        }

        // Open rather than merely recent: a finished poll does not stand in the
        // way of the next one.
        var alreadyRunning = await database.ListPolls.AnyAsync(
            existing => existing.ListId == list.Id && existing.Deadline > now,
            cancellationToken);

        if (alreadyRunning)
        {
            return Failed(StartPollOutcome.PollAlreadyActive);
        }

        var poll = new ListPoll
        {
            ListId = list.Id,
            CreatedById = userId,
            Deadline = deadline,
        };

        database.ListPolls.Add(poll);

        foreach (var itemId in candidates)
        {
            database.ListPollCandidates.Add(new ListPollCandidate
            {
                PollId = poll.Id,
                ListItemId = itemId,
            });
        }

        // One SaveChanges, so a poll never exists with nothing to vote on.
        await database.SaveChangesAsync(cancellationToken);

        return new StartPollResult(StartPollOutcome.Started, poll.Id);
    }

    public async Task<CastVoteOutcome> VoteAsync(
        ListPoll poll,
        Guid candidateId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id
            ?? throw new InvalidOperationException("Nobody cannot vote.");

        if (!poll.IsOpenAt(DateTime.UtcNow))
        {
            return CastVoteOutcome.PollClosed;
        }

        // Matched against the poll rather than taken on trust, so a candidate
        // id belonging to some other poll cannot be voted into this one.
        var belongs = await database.ListPollCandidates.AnyAsync(
            candidate => candidate.Id == candidateId && candidate.PollId == poll.Id,
            cancellationToken);

        if (!belongs)
        {
            return CastVoteOutcome.InvalidCandidate;
        }

        var vote = await database.ListPollVotes.FirstOrDefaultAsync(
            existing => existing.PollId == poll.Id && existing.UserId == userId,
            cancellationToken);

        if (vote is null)
        {
            database.ListPollVotes.Add(new ListPollVote
            {
                PollId = poll.Id,
                CandidateId = candidateId,
                UserId = userId,
            });
        }
        else
        {
            // Rewritten, not added to. One vote each is the rule; changing your
            // mind is not a second vote.
            vote.CandidateId = candidateId;
            vote.CreatedAt = DateTime.UtcNow;
        }

        await database.SaveChangesAsync(cancellationToken);

        return CastVoteOutcome.Recorded;
    }

    private static StartPollResult Failed(StartPollOutcome outcome) => new(outcome, PollId: null);
}
