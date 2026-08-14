using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Library;
using Movie.Domain.Library;
using Movie.Domain.Media;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Library;

/// <inheritdoc cref="ISavedMediaStore"/>
public sealed class SavedMediaStore(MovieDbContext context, ICurrentUser currentUser)
    : ISavedMediaStore
{
    public async Task<IReadOnlyList<SavedMedia>> ListAsync(
        ListType listType,
        CancellationToken cancellationToken = default) =>
        await context.SavedMedia
            .Where(x => x.ListType == listType)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<int> SaveAsync(
        IReadOnlyList<TitleSnapshot> titles,
        ListType listType,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId || titles.Count == 0)
        {
            return 0;
        }

        try
        {
            return await InsertMissingAsync(titles, listType, userId, cancellationToken);
        }
        catch (DbUpdateException e) when (UniqueViolations.Caused(e))
        {
            // Two imports running at once can each read before the other
            // writes. One retry settles it: the second read sees the rows the
            // first request inserted and skips them.
            context.ForgetPendingInserts<SavedMedia>();
            return await InsertMissingAsync(titles, listType, userId, cancellationToken);
        }
    }

    public async Task<bool> RemoveAsync(
        int mediaId,
        MediaType mediaType,
        ListType listType,
        CancellationToken cancellationToken = default) =>

        // The ownership filter applies to this the same as to a read, so the
        // delete cannot reach past the caller's own rows.
        await context.SavedMedia
            .Where(x => x.MediaId == mediaId
                && x.MediaType == mediaType
                && x.ListType == listType)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    private async Task<int> InsertMissingAsync(
        IReadOnlyList<TitleSnapshot> titles,
        ListType listType,
        Guid userId,
        CancellationToken cancellationToken)
    {
        // Read first rather than letting the unique index refuse the write, so
        // a re-run of the importer skips what is already there instead of
        // failing on the first title it has seen before.
        var saved = await context.SavedMedia
            .Where(x => x.ListType == listType)
            .Select(x => new { x.MediaId, x.MediaType })
            .ToListAsync(cancellationToken);

        var seen = saved.Select(x => (x.MediaId, x.MediaType)).ToHashSet();

        var rows = new List<SavedMedia>();

        foreach (var title in titles)
        {
            // Adding to the same set as it goes also covers a payload that
            // repeats a title inside itself, which the index would refuse just
            // as readily.
            if (!seen.Add((title.MediaId, title.MediaType)))
            {
                continue;
            }

            rows.Add(new SavedMedia
            {
                UserId = userId,
                ListType = listType,
                MediaId = title.MediaId,
                MediaType = title.MediaType,
                Title = title.Title,
                PosterPath = title.PosterPath,
                VoteAverage = title.VoteAverage,
                Year = title.Year,
                Genres = title.Genres,
            });
        }

        if (rows.Count == 0)
        {
            return 0;
        }

        context.SavedMedia.AddRange(rows);
        await context.SaveChangesAsync(cancellationToken);

        return rows.Count;
    }
}
