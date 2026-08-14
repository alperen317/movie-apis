using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Library;
using Movie.Domain.Library;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Library;

/// <inheritdoc cref="IWatchLogStore"/>
public sealed class WatchLogStore(MovieDbContext context, ICurrentUser currentUser)
    : IWatchLogStore
{
    public async Task<IReadOnlyList<WatchLogEntry>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await context.WatchLog
            .OrderByDescending(x => x.WatchedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WatchLogEntry>> AddAsync(
        IReadOnlyList<LoggedWatch> watches,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId || watches.Count == 0)
        {
            return [];
        }

        // A plain insert with nothing to reconcile. The table has no unique
        // index, because watching something twice is two events rather than
        // one event recorded twice.
        var rows = watches
            .Select(watch => new WatchLogEntry
            {
                UserId = userId,
                MediaId = watch.Title.MediaId,
                MediaType = watch.Title.MediaType,
                Title = watch.Title.Title,
                PosterPath = watch.Title.PosterPath,
                VoteAverage = watch.Title.VoteAverage,
                Year = watch.Title.Year,
                Genres = watch.Title.Genres,
                WatchedAt = watch.WatchedAt,
                Rating = watch.Rating,
                Note = watch.Note,
            })
            .ToList();

        context.WatchLog.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);

        return rows;
    }

    public async Task<WatchLogEntry?> UpdateAsync(
        Guid id,
        DateTime watchedAt,
        int? rating,
        string? note,
        CancellationToken cancellationToken = default)
    {
        // The ownership filter is what makes someone else's entry not there,
        // rather than found and then refused. There is no check to forget.
        var entry = await context.WatchLog
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        // The title itself is not editable. What is being corrected is the
        // record of watching it, not what was watched.
        entry.WatchedAt = watchedAt;
        entry.Rating = rating;
        entry.Note = note;

        await context.SaveChangesAsync(cancellationToken);

        return entry;
    }

    public async Task<int> RemoveAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        return await context.WatchLog
            .Where(x => ids.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
