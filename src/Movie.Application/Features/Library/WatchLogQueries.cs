using Mediator;

using Movie.Application.Abstractions.Library;
using Movie.Domain.Library;
using Movie.Domain.Media;

namespace Movie.Application.Features.Library;

/// <summary>
/// The caller's whole diary, newest watch first.
/// </summary>
public sealed record GetWatchLogQuery : IRequest<IReadOnlyList<WatchLogDto>>;

/// <param name="LogId">
/// The row's id, which unlike saved media does cross the wire: a title can
/// appear many times, so an entry can only be addressed by its own id.
/// </param>
/// <param name="Id">TMDB's id for the title that was watched.</param>
public sealed record WatchLogDto(
    Guid LogId,
    int Id,
    MediaType MediaType,
    string Title,
    string? PosterPath,
    decimal? VoteAverage,
    string? Year,
    string[] Genres,
    DateTime WatchedAt,
    int? Rating,
    string? Note)
{
    public static WatchLogDto From(WatchLogEntry entry) => new(
        entry.Id,
        entry.MediaId,
        entry.MediaType,
        entry.Title,
        entry.PosterPath,
        entry.VoteAverage,
        entry.Year,
        entry.Genres,
        entry.WatchedAt,
        entry.Rating,
        entry.Note);
}

public sealed class GetWatchLogQueryHandler(IWatchLogStore watchLog)
    : IRequestHandler<GetWatchLogQuery, IReadOnlyList<WatchLogDto>>
{
    public async ValueTask<IReadOnlyList<WatchLogDto>> Handle(
        GetWatchLogQuery query,
        CancellationToken cancellationToken)
    {
        var entries = await watchLog.ListAsync(cancellationToken);

        return [.. entries.Select(WatchLogDto.From)];
    }
}