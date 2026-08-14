using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Movie.Application.Abstractions.Library;
using Movie.Application.Features.Library;

namespace Movie.Api.Endpoints;

/// <summary>
/// Episode-level watch state for shows.
/// </summary>
/// <remarks>
/// The routes name the episode rather than a row id, because the row has no
/// identity beyond the episode it marks — its key is the (user, show, season,
/// episode) tuple.
/// </remarks>
public static class EpisodeProgressEndpoints
{
    public static void MapEpisodeProgressEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/episode-progress").RequireAuthorization().WithTags("Library");

        group.MapGet("/", GetProgress);
        group.MapPost("/batch", MarkBatch);
        group.MapPut("/{showId:int}/{season:int}/{episode:int}", MarkEpisode);
        group.MapDelete("/{showId:int}/{season:int}/{episode:int}", UnmarkEpisode);
        group.MapDelete("/{showId:int}/{season:int}", UnmarkSeason);
    }

    private static async Task<IResult> GetProgress(ISender sender) =>
        Results.Ok(await sender.Send(new GetEpisodeProgressQuery()));

    /// <summary>
    /// A PUT because it says what the state should be rather than adding to it:
    /// marking an episode already marked is the same request with the same
    /// result.
    /// </summary>
    private static async Task<IResult> MarkEpisode(
        int showId,
        int season,
        int episode,

        // Optional, so a caller with nothing to say beyond "I watched this"
        // can send no body at all.
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] WatchedAtRequest? request,
        ISender sender)
    {
        if (!TryWatchedAt(request?.WatchedAt, out var watchedAt))
        {
            return Timestamps.NotAnInstant(nameof(WatchedAtRequest.WatchedAt));
        }

        await sender.Send(new MarkEpisodesWatchedCommand(
            showId,
            [new Episode(season, episode)],
            watchedAt));

        return Results.NoContent();
    }

    /// <summary>
    /// "I have watched everything up to here", which the client sends as the
    /// episodes it worked out rather than as a high-water mark. Seasons vary in
    /// length and are not always numbered from one, so only the client knows
    /// what "up to here" covers.
    /// </summary>
    private static async Task<IResult> MarkBatch(MarkBatchRequest request, ISender sender)
    {
        if (request.Episodes.Length > Batches.MaxEpisodes)
        {
            return Batches.TooLarge(Batches.MaxEpisodes, "episodes");
        }

        if (!TryWatchedAt(request.WatchedAt, out var watchedAt))
        {
            return Timestamps.NotAnInstant(nameof(request.WatchedAt));
        }

        await sender.Send(new MarkEpisodesWatchedCommand(
            request.ShowId,
            [.. request.Episodes.Select(x => new Episode(x.SeasonNumber, x.EpisodeNumber))],
            watchedAt));

        return Results.NoContent();
    }

    /// <summary>
    /// An absent time means now, which is already an instant and needs no
    /// interpreting. A supplied one has to name one — see
    /// <see cref="Timestamps"/>.
    /// </summary>
    private static bool TryWatchedAt(DateTime? sent, out DateTime watchedAt)
    {
        if (sent is not { } value)
        {
            watchedAt = DateTime.UtcNow;
            return true;
        }

        return Timestamps.TryToUtc(value, out watchedAt);
    }

    private static async Task<IResult> UnmarkEpisode(
        int showId,
        int season,
        int episode,
        ISender sender)
    {
        await sender.Send(new UnmarkEpisodesCommand(showId, season, episode));

        // Unmarking what was never marked counts as done, so a retry does not
        // fail and the end state is the same either way.
        return Results.NoContent();
    }

    private static async Task<IResult> UnmarkSeason(int showId, int season, ISender sender)
    {
        // The same command with no episode named. Nothing distinguishes the two
        // beyond how much of the key is supplied.
        await sender.Send(new UnmarkEpisodesCommand(showId, season, EpisodeNumber: null));

        return Results.NoContent();
    }

    /// <param name="WatchedAt">
    /// Absent means now. Present lets an import keep the date the watch
    /// actually happened.
    /// </param>
    public sealed record WatchedAtRequest(DateTime? WatchedAt);

    public sealed record MarkBatchRequest(
        int ShowId,
        EpisodeRequest[] Episodes,
        DateTime? WatchedAt);

    public sealed record EpisodeRequest(int SeasonNumber, int EpisodeNumber);
}
