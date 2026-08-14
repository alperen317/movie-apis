using Mediator;
using Movie.Application.Abstractions.Library;
using Movie.Domain.Library;
using Movie.Domain.Media;

namespace Movie.Application.Features.Library;

/// <summary>
/// One of the caller's two personal lists. Which one is the only parameter —
/// whose is settled by the request.
/// </summary>
public sealed record GetSavedMediaQuery(ListType ListType)
    : IRequest<IReadOnlyList<SavedMediaDto>>;

/// <summary>
/// Flattened for the client, which reads these straight into a media card.
/// <c>Id</c> is the TMDB id, not the row's — the row's is of no use to anyone,
/// since a saved title is addressed by what it is.
/// </summary>
public sealed record SavedMediaDto(
    int Id,
    MediaType MediaType,
    string Title,
    string? PosterPath,
    decimal? VoteAverage,
    string? Year,
    string[] Genres,
    DateTime SavedAt)
{
    public static SavedMediaDto From(SavedMedia saved) => new(
        saved.MediaId,
        saved.MediaType,
        saved.Title,
        saved.PosterPath,
        saved.VoteAverage,
        saved.Year,
        saved.Genres,
        saved.CreatedAt);
}

public sealed class GetSavedMediaQueryHandler(ISavedMediaStore saved)
    : IRequestHandler<GetSavedMediaQuery, IReadOnlyList<SavedMediaDto>>
{
    public async ValueTask<IReadOnlyList<SavedMediaDto>> Handle(
        GetSavedMediaQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await saved.ListAsync(query.ListType, cancellationToken);

        return [.. rows.Select(SavedMediaDto.From)];
    }
}
