using Microsoft.EntityFrameworkCore;

using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Library;
using Movie.Infrastructure.Persistence;

using Progress = Movie.Domain.Library.EpisodeProgress;

namespace Movie.Infrastructure.Library;

/// <inheritdoc cref="IEpisodeProgressStore"/>
public sealed class EpisodeProgressStore(MovieDbContext context, ICurrentUser currentUser)
    : IEpisodeProgressStore
{
    public async Task<IReadOnlyList<Progress>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await context.EpisodeProgress
            .OrderByDescending(x => x.WatchedAt)
            .ToListAsync(cancellationToken);

    public async Task MarkAsync(
        int showId,
        IReadOnlyList<Episode> episodes,
        DateTime watchedAt,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId || episodes.Count == 0)
        {
            return;
        }

        try
        {
            await UpsertAsync(showId, episodes, watchedAt, userId, cancellationToken);
        }
        catch (DbUpdateException e) when (UniqueViolations.Caused(e))
        {
            // Two devices marking the same show at once can each read before
            // the other writes. One retry settles it: the second read sees the
            // rows the other request inserted and updates them instead.
            context.ForgetPendingInserts<Progress>();
            await UpsertAsync(showId, episodes, watchedAt, userId, cancellationToken);
        }
    }

    public async Task<int> UnmarkAsync(
        int showId,
        int seasonNumber,
        int? episodeNumber,
        CancellationToken cancellationToken = default) =>
        await context.EpisodeProgress
            .Where(x => x.ShowId == showId
                && x.SeasonNumber == seasonNumber

                // An absent episode number means the whole season, so the
                // comparison drops out of the query rather than matching
                // nothing.
                && (episodeNumber == null || x.EpisodeNumber == episodeNumber))
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// The upsert the primary key makes possible: an episode already marked has
    /// its time overwritten, one that is not is inserted.
    /// </summary>
    private async Task UpsertAsync(
        int showId,
        IReadOnlyList<Episode> episodes,
        DateTime watchedAt,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // One read for the whole show rather than one per episode, which is
        // what makes marking a season a single round trip.
        var marked = await context.EpisodeProgress
            .Where(x => x.ShowId == showId)
            .ToListAsync(cancellationToken);

        var byEpisode = marked.ToDictionary(x => new Episode(x.SeasonNumber, x.EpisodeNumber));

        foreach (var episode in episodes)
        {
            if (byEpisode.TryGetValue(episode, out var already))
            {
                already.WatchedAt = watchedAt;
                continue;
            }

            var progress = new Progress
            {
                UserId = userId,
                ShowId = showId,
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumber = episode.EpisodeNumber,
                WatchedAt = watchedAt,
            };

            context.EpisodeProgress.Add(progress);

            // Recorded as it goes, so a payload that repeats an episode does
            // not try to insert it twice — which the key would refuse.
            byEpisode[episode] = progress;
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}