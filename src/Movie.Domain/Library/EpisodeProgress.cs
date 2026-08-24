namespace Movie.Domain.Library;

/// <summary>
/// Episode-level watch state. It has no identity of its own: the primary key is
/// the (user, show, season, episode) tuple. "Mark everything up to here" lands
/// on this table as a multi-row upsert.
/// </summary>
/// <remarks>
/// Deliberately does not use <see cref="Media.MediaSnapshot"/> — no TMDB title
/// data is stored here, only which episode was watched. Title data comes from
/// the show's <see cref="WatchLogEntry"/> or from TMDB when needed.
/// </remarks>
public sealed class EpisodeProgress
{
    public required Guid UserId { get; init; }

    /// <summary>The show's TMDB id.</summary>
    public required int ShowId { get; init; }

    public required int SeasonNumber { get; init; }

    public required int EpisodeNumber { get; init; }

    /// <summary><c>set</c> because upserts overwrite it.</summary>
    public DateTime WatchedAt { get; set; } = DateTime.UtcNow;
}