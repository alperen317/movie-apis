namespace Movie.Domain.Media;

/// <summary>
/// A TMDB title as it looked when it was saved. The same seven columns repeat
/// across <c>saved_media</c>, <c>watch_log</c> and <c>list_items</c>, so they
/// are factored into one owned type — the columns still live on the owner's
/// own table under the same names, only the C# side is shared.
/// </summary>
/// <remarks>
/// The name "snapshot" is deliberate: this is a copy taken at a point in time,
/// not a live reference. If TMDB later changes a title, the user still sees
/// what they saved. Every field is <c>init</c>.
/// </remarks>
public sealed class MediaSnapshot
{
    public required int MediaId { get; init; }

    public required MediaType MediaType { get; init; }

    public required string Title { get; init; }

    public string? PosterPath { get; init; }

    /// <summary>TMDB's score out of 10. Null for unrated titles.</summary>
    public decimal? VoteAverage { get; init; }

    /// <summary>
    /// Release year. Text rather than a number: TMDB omits it for some titles
    /// and reports a range for some shows.
    /// </summary>
    public string? Year { get; init; }

    /// <summary>
    /// Every genre name TMDB returned. Stored as <c>text[]</c> in Postgres.
    /// An empty array is a valid value; null is not.
    /// </summary>
    public string[] Genres { get; init; } = [];
}
