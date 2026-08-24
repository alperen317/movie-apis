using Movie.Domain.Media;

namespace Movie.Domain.Library;

/// <summary>
/// A title saved to favorites or the watchlist. Unique per user, list type and
/// title: the same film can't be favorited twice, but it can sit in favorites
/// and the watchlist at once.
/// </summary>
/// <remarks>
/// <para>
/// Every field is <c>init</c>: this table had no UPDATE policy in Supabase at
/// all (only select/insert/delete), so a row never changed after being written.
/// Users remove and re-add instead.
/// </para>
/// <para>
/// The seven TMDB fields below repeat on <see cref="WatchLogEntry"/> and
/// <c>ListItem</c>. They were briefly factored into a shared complex type, but
/// EF Core cannot build an index spanning an entity and a complex type, and the
/// uniqueness rule here needs exactly that. Keeping them flat keeps every
/// constraint inside the EF model.
/// </para>
/// </remarks>
public sealed class SavedMedia
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required ListType ListType { get; init; }

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

    /// <summary>Stored as <c>text[]</c>. An empty array is valid; null is not.</summary>
    public string[] Genres { get; init; } = [];

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}