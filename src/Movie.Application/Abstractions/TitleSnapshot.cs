using Movie.Domain.Media;

namespace Movie.Application.Abstractions;

/// <summary>
/// The TMDB fields a caller supplies when saving a title, carried alongside the
/// id so the app can render a poster without a second round trip.
/// </summary>
/// <remarks>
/// A transport shape, not a persisted one. The same seven fields are stored on
/// <c>SavedMedia</c>, <c>WatchLogEntry</c> and <c>ListItem</c>, but as flat
/// columns on each — see <c>SavedMedia</c> for why they are not a shared
/// complex type.
/// </remarks>
public sealed record TitleSnapshot(
    int MediaId,
    MediaType MediaType,
    string Title,
    string? PosterPath,
    decimal? VoteAverage,
    string? Year,
    string[] Genres);