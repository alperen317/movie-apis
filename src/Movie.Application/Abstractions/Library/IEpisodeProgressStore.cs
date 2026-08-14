using Movie.Domain.Library;

namespace Movie.Application.Abstractions.Library;

/// <summary>
/// Which episodes of which shows the caller has watched.
/// </summary>
public interface IEpisodeProgressStore
{
    Task<IReadOnlyList<EpisodeProgress>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks episodes watched, overwriting the time on any already marked.
    /// </summary>
    /// <remarks>
    /// One method for one episode and for a whole season, because "mark
    /// everything up to here" is the same upsert with a longer list. The row
    /// has no identity beyond the episode it names, which is what makes that
    /// true.
    /// </remarks>
    Task MarkAsync(
        int showId,
        IReadOnlyList<Episode> episodes,
        DateTime watchedAt,
        CancellationToken cancellationToken = default);

    /// <param name="episodeNumber">
    /// Null to unmark the whole season. The two cases differ only in how much
    /// of the key is supplied.
    /// </param>
    /// <returns>How many episodes stopped being marked.</returns>
    Task<int> UnmarkAsync(
        int showId,
        int seasonNumber,
        int? episodeNumber,
        CancellationToken cancellationToken = default);
}

/// <summary>An episode within a show, which the show id supplies separately.</summary>
public readonly record struct Episode(int SeasonNumber, int EpisodeNumber);
