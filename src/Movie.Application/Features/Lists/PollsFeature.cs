using Mediator;

using Movie.Application.Abstractions.Lists;

namespace Movie.Application.Features.Lists;

/// <summary>
/// A list's most recent poll, running or just finished.
/// </summary>
public sealed record GetListPollQuery(Guid ListId) : IRequest<GetListPollResult>;

/// <param name="Poll">
/// Null when the list has never had a poll — which is not the same as the
/// caller not being able to see the list, hence <paramref name="Visible"/>.
/// </param>
public sealed record GetListPollResult(bool Visible, ListPollDto? Poll)
{
    public static GetListPollResult Unreachable => new(Visible: false, Poll: null);

    public static GetListPollResult None => new(Visible: true, Poll: null);
}

/// <param name="Deadline">
/// Whether the poll is over is left to the reader to work out from this. It is
/// not stored, so there is nothing to go stale and no job to keep it fresh.
/// </param>
public sealed record ListPollDto(
    Guid Id,
    DateTime Deadline,
    Guid CreatedBy,
    IReadOnlyList<PollCandidateDto> Candidates)
{
    public static ListPollDto From(PollSnapshot poll) => new(
        poll.PollId,
        poll.Deadline,
        poll.CreatedById,
        [.. poll.Candidates.Select(PollCandidateDto.From)]);
}

public sealed record PollCandidateDto(Guid Id, Guid ListItemId, int VoteCount, bool MyVote)
{
    public static PollCandidateDto From(PollCandidateTally candidate) => new(
        candidate.CandidateId,
        candidate.ListItemId,
        candidate.VoteCount,
        candidate.MyVote);
}

public sealed class GetListPollQueryHandler(IListAccess access, IPollStore polls)
    : IRequestHandler<GetListPollQuery, GetListPollResult>
{
    public async ValueTask<GetListPollResult> Handle(
        GetListPollQuery query,
        CancellationToken cancellationToken)
    {
        var list = await access.ForMemberAsync(query.ListId, cancellationToken);

        if (list is null)
        {
            return GetListPollResult.Unreachable;
        }

        var poll = await polls.LatestAsync(list, cancellationToken);

        return poll is null ? GetListPollResult.None : new GetListPollResult(true, ListPollDto.From(poll));
    }
}

/// <summary>
/// Opens a poll. Any member may — deciding what to watch is not the creator's
/// privilege.
/// </summary>
public sealed record StartListPollCommand(
    Guid ListId,
    DateTime Deadline,
    IReadOnlyList<Guid> ItemIds) : IRequest<StartPollResult?>;

public sealed class StartListPollCommandHandler(
    IListAccess access,
    IPollStore polls,
    IListEventPublisher events)
    : IRequestHandler<StartListPollCommand, StartPollResult?>
{
    /// <returns>Null when the caller cannot reach the list at all.</returns>
    public async ValueTask<StartPollResult?> Handle(
        StartListPollCommand command,
        CancellationToken cancellationToken)
    {
        var list = await access.ForMemberAsync(command.ListId, cancellationToken);

        if (list is null)
        {
            return null;
        }

        var result = await polls.StartAsync(list, command.Deadline, command.ItemIds, cancellationToken);

        if (result.Outcome == StartPollOutcome.Started)
        {
            await events.PollUpdatedAsync(list.Id, cancellationToken);
        }

        return result;
    }
}

/// <summary>
/// Casts a vote, or moves one already cast.
/// </summary>
public sealed record CastPollVoteCommand(Guid PollId, Guid CandidateId)
    : IRequest<CastVoteOutcome?>;

public sealed class CastPollVoteCommandHandler(
    IListAccess access,
    IPollStore polls,
    IListEventPublisher events)
    : IRequestHandler<CastPollVoteCommand, CastVoteOutcome?>
{
    /// <returns>Null when there is no such poll of the caller's to vote in.</returns>
    public async ValueTask<CastVoteOutcome?> Handle(
        CastPollVoteCommand command,
        CancellationToken cancellationToken)
    {
        // Membership resolved through the poll's own list. A poll carries no
        // list id the caller could supply, which is exactly why it is reached
        // this way rather than by being told which list to check.
        var poll = await access.PollForMemberAsync(command.PollId, cancellationToken);

        if (poll is null)
        {
            return null;
        }

        var outcome = await polls.VoteAsync(poll, command.CandidateId, cancellationToken);

        if (outcome == CastVoteOutcome.Recorded)
        {
            await events.PollUpdatedAsync(poll.ListId, cancellationToken);
        }

        return outcome;
    }
}