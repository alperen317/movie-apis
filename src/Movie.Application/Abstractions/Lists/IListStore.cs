using Movie.Domain.Lists;
using Movie.Domain.Media;

namespace Movie.Application.Abstractions.Lists;

/// <summary>
/// Reading and writing a shared list once the caller has been shown to be
/// allowed to.
/// </summary>
/// <remarks>
/// <para>
/// Every method that touches one list takes the <see cref="MediaList"/> itself
/// rather than an id. That is the point: the only way to hold one is to have
/// asked <see cref="IListAccess"/> for it, so passing it here carries the proof
/// that the check happened. An id would let a handler reach a list it never
/// established the caller could see.
/// </para>
/// <para>
/// The two methods that take no list are the ones with nothing to check
/// against: what the caller belongs to, and making something new.
/// </para>
/// </remarks>
public interface IListStore
{
    /// <summary>
    /// The lists the caller has joined, newest first.
    /// </summary>
    /// <remarks>
    /// Read through memberships rather than through the lists table. A list is
    /// visible to someone still deciding on an invitation, so asking the lists
    /// table "which can I see" would blend those in with the ones actually
    /// joined — the same reason the Supabase client queried
    /// <c>list_members</c>.
    /// </remarks>
    Task<IReadOnlyList<MediaList>> MineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a list and the creator's own membership in it.
    /// </summary>
    /// <remarks>
    /// Both rows or neither. A list whose creator is not a member of it would
    /// be one nobody could read.
    /// </remarks>
    Task<MediaList> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task RenameAsync(MediaList list, string name, CancellationToken cancellationToken = default);

    /// <summary>Takes the members, items and polls with it.</summary>
    Task DeleteAsync(MediaList list, CancellationToken cancellationToken = default);

    /// <summary>
    /// The whole roster, oldest first — including invitations still unanswered,
    /// which is what lets the members screen show who has yet to reply.
    /// </summary>
    Task<IReadOnlyList<ListMember>> MembersAsync(
        MediaList list,
        CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(ListMember membership, CancellationToken cancellationToken = default);

    /// <summary>Newest addition first.</summary>
    Task<IReadOnlyList<ListItem>> ItemsAsync(
        MediaList list,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a title, or hands back the one already there.
    /// </summary>
    /// <remarks>
    /// Adding something a co-member added a moment ago is not a failure — the
    /// list already holds it, which is what the caller wanted. Returning the
    /// existing row rather than an error also answers the question the caller
    /// would ask next, which is who put it there.
    /// </remarks>
    Task<ListItem> AddItemAsync(
        MediaList list,
        TitleSnapshot title,
        CancellationToken cancellationToken = default);

    /// <remarks>
    /// Open to every accepted member regardless of who added the title. That is
    /// a product decision rather than an oversight: members are equals when
    /// editing content, and <c>ListItem.AddedById</c> is only ever displayed.
    /// </remarks>
    Task<bool> RemoveItemAsync(
        MediaList list,
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of a list's members have already seen each of its titles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one place in the application that reads other people's watch log,
    /// and the only reason the ownership filter on that table has an exception
    /// at all. It exists because "three of us have seen this" is what a group
    /// deciding what to watch actually wants to know.
    /// </para>
    /// <para>
    /// <strong>Counts only.</strong> Never a row, never a date, never a rating
    /// or a note. A member's own history stays theirs; what a co-member learns
    /// is a number. The Supabase function was written under exactly this
    /// constraint and said so, which is why it returned an aggregate and not
    /// the rows it aggregated.
    /// </para>
    /// <para>
    /// Titles nobody has seen are absent rather than reported as zero, which is
    /// what the client already assumes for a key it does not find.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<WatchedCount>> WatchSummaryAsync(
        MediaList list,
        CancellationToken cancellationToken = default);
}

/// <param name="Count">
/// Distinct members, not entries — somebody who rewatched a film four times has
/// still seen it once as far as the group is concerned.
/// </param>
public sealed record WatchedCount(int MediaId, MediaType MediaType, int Count);
