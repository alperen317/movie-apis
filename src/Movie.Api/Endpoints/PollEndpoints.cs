using Mediator;
using Movie.Application.Abstractions.Lists;
using Movie.Application.Features.Lists;
using Movie.Domain.Lists;

namespace Movie.Api.Endpoints;

/// <summary>
/// Polls, and the watch summary that helps a group decide without one.
/// </summary>
public static class PollEndpoints
{
    public static void MapPollEndpoints(this IEndpointRouteBuilder app)
    {
        var lists = app.MapGroup("/lists").RequireAuthorization().WithTags("Polls");

        lists.MapGet("/{id:guid}/poll", GetPoll);
        lists.MapPost("/{id:guid}/polls", StartPoll);
        lists.MapGet("/{id:guid}/watch-summary", GetWatchSummary);

        // A poll is voted in by its own id: it carries the list it belongs to,
        // so there is no list for the caller to name — or to name wrongly.
        var polls = app.MapGroup("/polls").RequireAuthorization().WithTags("Polls");

        polls.MapPost("/{pollId:guid}/votes", Vote);
    }

    private static async Task<IResult> GetPoll(Guid id, ISender sender)
    {
        var result = await sender.Send(new GetListPollQuery(id));

        return result switch
        {
            { Visible: false } => Results.NotFound(),

            // A list that has never had a poll is not a missing list. The two
            // would be indistinguishable as 404s, and only one of them means
            // the caller should stop asking.
            { Poll: null } => Results.NoContent(),

            _ => Results.Ok(result.Poll),
        };
    }

    private static async Task<IResult> StartPoll(Guid id, StartPollRequest request, ISender sender)
    {
        if (!Timestamps.TryToUtc(request.Deadline, out var deadline))
        {
            return Timestamps.NotAnInstant(nameof(request.Deadline));
        }

        var result = await sender.Send(
            new StartListPollCommand(id, deadline, request.ItemIds ?? []));

        if (result is null)
        {
            return Results.NotFound();
        }

        return result.Outcome switch
        {
            StartPollOutcome.Started =>
                Results.Created($"/lists/{id}/poll", new StartPollResponse(result.PollId!.Value)),

            StartPollOutcome.PollAlreadyActive => Conflict(
                "poll_already_active",
                "This list already has an active poll."),

            StartPollOutcome.DeadlineNotInFuture => Conflict(
                "invalid_deadline",
                "Pick a deadline in the future."),

            StartPollOutcome.NeedAtLeastTwoCandidates => Conflict(
                "need_at_least_two_candidates",
                $"Pick at least {ListPoll.MinimumCandidates} titles to vote on."),

            // Refused rather than quietly dropped: a poll missing a candidate
            // the caller thought they had picked is worse than one refused.
            StartPollOutcome.UnknownCandidates => Conflict(
                "invalid_candidate",
                "Every candidate has to be a title already on this list."),

            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> Vote(Guid pollId, VoteRequest request, ISender sender)
    {
        var outcome = await sender.Send(new CastPollVoteCommand(pollId, request.CandidateId));

        return outcome switch
        {
            CastVoteOutcome.Recorded => Results.NoContent(),

            CastVoteOutcome.PollClosed => Conflict(
                "poll_closed",
                "This poll has already closed."),

            CastVoteOutcome.InvalidCandidate => Conflict(
                "invalid_candidate",
                "That isn't one of this poll's candidates."),

            // No such poll, or none the caller is a member for. One answer, as
            // everywhere else a list is out of reach.
            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> GetWatchSummary(Guid id, ISender sender)
    {
        var summary = await sender.Send(new GetWatchSummaryQuery(id));

        return summary is null ? Results.NotFound() : Results.Ok(summary);
    }

    private static IResult Conflict(string error, string message) =>
        Results.Conflict(new { error, message });

    /// <param name="ItemIds">
    /// Ids of items already on the list, not TMDB ids. A candidate points at
    /// the item so that removing the title from the list withdraws it from the
    /// poll as well.
    /// </param>
    public sealed record StartPollRequest(DateTime Deadline, Guid[]? ItemIds);

    public sealed record StartPollResponse(Guid PollId);

    public sealed record VoteRequest(Guid CandidateId);
}
