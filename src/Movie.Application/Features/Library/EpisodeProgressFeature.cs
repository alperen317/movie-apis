using Mediator;
using Movie.Application.Abstractions.Library;

namespace Movie.Application.Features.Library;

/// <summary>
/// Every episode the caller has marked watched, across every show. The client
/// holds the whole set and answers "how far am I into this?" from it.
/// </summary>
public sealed record GetEpisodeProgressQuery : IRequest<IReadOnlyList<EpisodeProgressDto>>;

public sealed record EpisodeProgressDto(
    int ShowId,
    int SeasonNumber,
    int EpisodeNumber,
    DateTime WatchedAt);

public sealed class GetEpisodeProgressQueryHandler(IEpisodeProgressStore progress)
    : IRequestHandler<GetEpisodeProgressQuery, IReadOnlyList<EpisodeProgressDto>>
{
    public async ValueTask<IReadOnlyList<EpisodeProgressDto>> Handle(
        GetEpisodeProgressQuery query,
        CancellationToken cancellationToken)
    {
        var marked = await progress.ListAsync(cancellationToken);

        return
        [
            .. marked.Select(x => new EpisodeProgressDto(
                x.ShowId,
                x.SeasonNumber,
                x.EpisodeNumber,
                x.WatchedAt)),
        ];
    }
}

/// <summary>
/// Marks episodes of one show watched. One episode or a whole season — the
/// difference is only the length of the list.
/// </summary>
public sealed record MarkEpisodesWatchedCommand(
    int ShowId,
    IReadOnlyList<Episode> Episodes,
    DateTime WatchedAt) : IRequest<Unit>;

public sealed class MarkEpisodesWatchedCommandHandler(IEpisodeProgressStore progress)
    : IRequestHandler<MarkEpisodesWatchedCommand, Unit>
{
    public async ValueTask<Unit> Handle(
        MarkEpisodesWatchedCommand command,
        CancellationToken cancellationToken)
    {
        await progress.MarkAsync(
            command.ShowId,
            command.Episodes,
            command.WatchedAt,
            cancellationToken);

        return Unit.Value;
    }
}

/// <summary>
/// Unmarks an episode, or a whole season when no episode is named.
/// </summary>
public sealed record UnmarkEpisodesCommand(int ShowId, int SeasonNumber, int? EpisodeNumber)
    : IRequest<int>;

public sealed class UnmarkEpisodesCommandHandler(IEpisodeProgressStore progress)
    : IRequestHandler<UnmarkEpisodesCommand, int>
{
    /// <returns>How many episodes stopped being marked.</returns>
    public ValueTask<int> Handle(
        UnmarkEpisodesCommand command,
        CancellationToken cancellationToken) =>
        new(progress.UnmarkAsync(
            command.ShowId,
            command.SeasonNumber,
            command.EpisodeNumber,
            cancellationToken));
}
