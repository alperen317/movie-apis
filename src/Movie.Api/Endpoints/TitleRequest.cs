using Movie.Application.Abstractions;
using Movie.Domain.Media;

namespace Movie.Api.Endpoints;

/// <summary>
/// The TMDB fields a caller sends when saving or logging a title. Shared by
/// <see cref="SavedMediaEndpoints"/> and <see cref="WatchLogEndpoints"/>,
/// because both store the same denormalised copy of a title.
/// </summary>
/// <param name="Id">
/// TMDB's id, named as the client names it. Saved titles are addressed by what
/// they are, so no row id ever crosses the wire for them.
/// </param>
public sealed record TitleRequest(
    int Id,
    MediaType MediaType,
    string Title,
    string? PosterPath,
    decimal? VoteAverage,
    string? Year,
    string[]? Genres)
{
    /// <remarks>
    /// A missing genres array becomes an empty one. The column is
    /// <c>not null</c> and an untagged title is perfectly ordinary, so refusing
    /// the request would be refusing something valid.
    /// </remarks>
    public TitleSnapshot ToSnapshot() => new(
        Id,
        MediaType,
        Title,
        PosterPath,
        VoteAverage,
        Year,
        Genres ?? []);

    /// <summary>
    /// Checks the title against the column widths it has to fit into, so an
    /// overlong field comes back as a 400 naming the field rather than as a
    /// database error.
    /// </summary>
    /// <param name="index">
    /// Which title in a batch this is, so the caller can find it. Null for the
    /// single-title endpoints, where there is nothing to point at.
    /// </param>
    /// <returns>Null when there is nothing wrong with it.</returns>
    public IResult? Validate(int? index = null)
    {
        var errors = new Dictionary<string, string[]>();
        var prefix = index is { } i ? $"[{i}]." : string.Empty;

        if (string.IsNullOrWhiteSpace(Title))
        {
            errors[$"{prefix}title"] = ["A title is required."];
        }
        else if (Title.Length > 500)
        {
            errors[$"{prefix}title"] = ["A title cannot be longer than 500 characters."];
        }

        if (PosterPath is { Length: > 255 })
        {
            errors[$"{prefix}posterPath"] = ["A poster path cannot be longer than 255 characters."];
        }

        if (Year is { Length: > 20 })
        {
            errors[$"{prefix}year"] = ["A year cannot be longer than 20 characters."];
        }

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }
}
