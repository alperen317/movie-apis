using Movie.Application.Features.Lists;
using Movie.Domain.Media;

namespace Movie.Application.Abstractions.Lists;

/// <summary>
/// Tells a list's connected members that something about it changed.
/// </summary>
/// <remarks>
/// <para>
/// The SignalR counterpart of Supabase's realtime publication. Handlers call
/// this after a mutation has already gone through <see cref="IListStore"/> or
/// <see cref="IInvitationStore"/> — never instead of it, and never before it
/// succeeds. A dropped or delayed notification only means a client refetches a
/// moment later than it could have; it is not the source of truth for anything.
/// </para>
/// <para>
/// Payloads are kept to what a client cannot cheaply recompute itself (the item
/// just added, the list's new name) or left as a bare signal when the honest
/// answer is "go re-fetch the roster" — <see cref="MembersChangedAsync"/> and
/// <see cref="PollUpdatedAsync"/> cover several distinct causes each, and
/// duplicating every one of those shapes here would only drift from the DTOs
/// the REST endpoints already return.
/// </para>
/// </remarks>
public interface IListEventPublisher
{
    /// <summary>A title was put on the list.</summary>
    Task ItemAddedAsync(Guid listId, ListItemDto item, CancellationToken cancellationToken = default);

    /// <summary>A title was taken off the list.</summary>
    Task ItemRemovedAsync(
        Guid listId,
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The roster changed — someone was invited, answered, joined by code, or
    /// was removed.
    /// </summary>
    Task MembersChangedAsync(Guid listId, CancellationToken cancellationToken = default);

    /// <summary>The list was renamed.</summary>
    Task ListRenamedAsync(Guid listId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// The list was deleted. Sent to the group before anyone is evicted from
    /// it, so a client that is currently looking at the list hears about it
    /// rather than being silently dropped.
    /// </summary>
    Task ListDeletedAsync(Guid listId, CancellationToken cancellationToken = default);

    /// <summary>A poll was started, or a vote moved its tally.</summary>
    Task PollUpdatedAsync(Guid listId, CancellationToken cancellationToken = default);
}