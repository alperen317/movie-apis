using Movie.Domain.Media;

namespace Movie.Domain.Library;

/// <summary>
/// A single watch event. Unlike <see cref="SavedMedia"/> there is <em>no</em>
/// uniqueness constraint — a title may appear many times, because rewatches are
/// supported by design.
/// </summary>
public sealed class WatchLogEntry
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required int MediaId { get; init; }

    public required MediaType MediaType { get; init; }

    public required string Title { get; init; }

    public string? PosterPath { get; init; }

    public decimal? VoteAverage { get; init; }

    public string? Year { get; init; }

    public string[] Genres { get; init; } = [];

    /// <summary>
    /// When the watch happened. May differ from <see cref="CreatedAt"/> because
    /// users can backdate entries, and can be corrected afterwards.
    /// </summary>
    public DateTime WatchedAt { get; set; }

    /// <summary>The user's own 1–10 score. Null when unrated.</summary>
    public int? Rating { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
