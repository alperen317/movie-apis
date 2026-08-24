using Movie.Domain.Library;

namespace Movie.Application.Abstractions.Library;

/// <summary>
/// The caller's diary of what they have watched.
/// </summary>
/// <remarks>
/// Unlike saved media there is no uniqueness here, because a rewatch is a
/// second real event rather than a duplicate. Nothing in this interface tries
/// to reconcile one entry with another.
/// </remarks>
public interface IWatchLogStore
{
    /// <summary>
    /// Newest watch first. Ordered by when the watch happened rather than when
    /// it was recorded, so a backdated entry lands where it belongs.
    /// </summary>
    Task<IReadOnlyList<WatchLogEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one or many watches and hands back the rows as written, which
    /// is how the caller learns the ids it needs to edit or delete them.
    /// </summary>
    Task<IReadOnlyList<WatchLogEntry>> AddAsync(
        IReadOnlyList<LoggedWatch> watches,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects an entry. Null when there is no such entry of the caller's —
    /// someone else's is not found and refused, it is simply not there.
    /// </summary>
    Task<WatchLogEntry?> UpdateAsync(
        Guid id,
        DateTime watchedAt,
        int? rating,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes entries by id and reports how many went.
    /// </summary>
    /// <remarks>
    /// Takes a list because unmarking a title as watched has to remove every
    /// entry for it rather than the latest. The flag the app shows means "is
    /// there any entry at all", so an earlier rewatch left behind would keep
    /// the title looking watched.
    /// </remarks>
    Task<int> RemoveAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);
}

/// <param name="WatchedAt">
/// When the watch happened, which the caller may backdate and may correct
/// later. Distinct from when the row was written.
/// </param>
/// <param name="Rating">The caller's own score out of ten. Null when unrated.</param>
public sealed record LoggedWatch(
    TitleSnapshot Title,
    DateTime WatchedAt,
    int? Rating,
    string? Note);