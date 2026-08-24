using Mediator;

using Microsoft.AspNetCore.Mvc;

using Movie.Application.Abstractions.Library;
using Movie.Application.Features.Library;

namespace Movie.Api.Endpoints;

/// <summary>
/// The diary: what the caller watched, when, and what they thought of it.
/// </summary>
public static class WatchLogEndpoints
{
    public static void MapWatchLogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/watch-log").RequireAuthorization().WithTags("Library");

        group.MapGet("/", GetWatchLog);
        group.MapPost("/", LogWatch);
        group.MapPost("/batch", LogWatchBatch);
        group.MapPut("/{id:guid}", UpdateEntry);
        group.MapDelete("/", RemoveEntries);
    }

    private static async Task<IResult> GetWatchLog(ISender sender) =>
        Results.Ok(await sender.Send(new GetWatchLogQuery()));

    private static async Task<IResult> LogWatch(LogWatchRequest request, ISender sender)
    {
        if (Invalid(request, index: null) is { } problem)
        {
            return problem;
        }

        var written = await sender.Send(new LogWatchCommand([request.ToWatch()]));

        // Handed back so the caller has the id it needs to edit or delete the
        // entry later, which it cannot derive from the title.
        return Results.Ok(written[0]);
    }

    /// <summary>For the TV Time / Letterboxd importer.</summary>
    private static async Task<IResult> LogWatchBatch(
        LogWatchRequest[] request,
        ISender sender)
    {
        if (request.Length > Batches.MaxTitles)
        {
            return Batches.TooLarge(Batches.MaxTitles, "watches");
        }

        for (var i = 0; i < request.Length; i++)
        {
            if (Invalid(request[i], i) is { } problem)
            {
                return problem;
            }
        }

        var written = await sender.Send(
            new LogWatchCommand([.. request.Select(x => x.ToWatch())]));

        return Results.Ok(new LogWatchBatchResponse(written.Count));
    }

    private static async Task<IResult> UpdateEntry(
        Guid id,
        UpdateEntryRequest request,
        ISender sender)
    {
        if (OutOfRange(request.Rating) is { } problem)
        {
            return problem;
        }

        if (!Timestamps.TryToUtc(request.WatchedAt, out var watchedAt))
        {
            return Timestamps.NotAnInstant(nameof(request.WatchedAt));
        }

        var updated = await sender.Send(new UpdateWatchLogEntryCommand(
            id,
            watchedAt,
            request.Rating,
            request.Note));

        // Someone else's entry is not there rather than forbidden, which is
        // also the only answer that does not confirm it exists.
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    /// <summary>
    /// Takes the ids in the body rather than the path because unmarking a title
    /// as watched deletes every entry for it — see
    /// <see cref="RemoveWatchLogEntriesCommand"/>.
    /// </summary>
    private static async Task<IResult> RemoveEntries(
        // Spelled out because a body is never inferred for DELETE — the method
        // has no defined payload semantics, so the framework refuses to guess.
        [FromBody] RemoveEntriesRequest request,
        ISender sender)
    {
        if (request.Ids.Length > Batches.MaxTitles)
        {
            return Batches.TooLarge(Batches.MaxTitles, "entries");
        }

        await sender.Send(new RemoveWatchLogEntriesCommand(request.Ids));

        // Ids that were not the caller's are simply not deleted. Reporting that
        // would be reporting whether someone else's entry exists.
        return Results.NoContent();
    }

    private static IResult? Invalid(LogWatchRequest request, int? index) =>
        request.Title.Validate(index)
        ?? OutOfRange(request.Rating, index)
        ?? (Timestamps.TryToUtc(request.WatchedAt, out _)
            ? null
            : Timestamps.NotAnInstant(nameof(request.WatchedAt), index));

    /// <summary>
    /// Kept in step with the <c>watch_log_rating_range</c> check constraint, so
    /// a bad score is a 400 rather than a failed write.
    /// </summary>
    private static IResult? OutOfRange(int? rating, int? index = null)
    {
        if (rating is null or (>= 1 and <= 10))
        {
            return null;
        }

        var prefix = index is { } i ? $"[{i}]." : string.Empty;

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [$"{prefix}rating"] = ["A rating must be between 1 and 10."],
        });
    }

    /// <param name="WatchedAt">
    /// When the watch happened. Sent by the caller rather than taken from the
    /// clock here, because entries can be backdated.
    /// </param>
    public sealed record LogWatchRequest(
        TitleRequest Title,
        DateTime WatchedAt,
        int? Rating,
        string? Note)
    {
        /// <remarks>
        /// Only sound after <see cref="Invalid"/> has passed the request, which
        /// is what establishes the timestamp names an instant at all.
        /// </remarks>
        public LoggedWatch ToWatch()
        {
            Timestamps.TryToUtc(WatchedAt, out var watchedAt);

            return new LoggedWatch(Title.ToSnapshot(), watchedAt, Rating, Note);
        }
    }

    public sealed record UpdateEntryRequest(DateTime WatchedAt, int? Rating, string? Note);

    public sealed record RemoveEntriesRequest(Guid[] Ids);

    /// <param name="Logged">How many entries were written.</param>
    public sealed record LogWatchBatchResponse(int Logged);
}