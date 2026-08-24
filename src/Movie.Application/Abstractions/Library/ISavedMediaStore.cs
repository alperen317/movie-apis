using Movie.Domain.Library;
using Movie.Domain.Media;

namespace Movie.Application.Abstractions.Library;

/// <summary>
/// Favorites and the watchlist, always the caller's own.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here takes a user id. Reads and deletes are already scoped by the
/// context's ownership filter, and writes stamp the owner from the request for
/// the same reason <see cref="Lists.IListAccess"/> resolves the caller itself:
/// a handler that cannot name another user cannot write a row for one.
/// </para>
/// </remarks>
public interface ISavedMediaStore
{
    /// <summary>Newest first, the order the library screen renders.</summary>
    Task<IReadOnlyList<SavedMedia>> ListAsync(
        ListType listType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the titles that are not saved already, and reports how many were
    /// new.
    /// </summary>
    /// <remarks>
    /// A title already in the list is skipped rather than refused. The endpoint
    /// means "make sure this is saved", the unique index already says a title
    /// is saved once, and the row that would be written is identical to the one
    /// already there — so there is nothing for an error to tell the caller.
    /// This is also what let the importer be re-run in Supabase.
    /// </remarks>
    Task<int> SaveAsync(
        IReadOnlyList<TitleSnapshot> titles,
        ListType listType,
        CancellationToken cancellationToken = default);

    /// <summary>Whether a row was actually removed.</summary>
    Task<bool> RemoveAsync(
        int mediaId,
        MediaType mediaType,
        ListType listType,
        CancellationToken cancellationToken = default);
}