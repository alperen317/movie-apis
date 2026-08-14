using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;
using Movie.Domain.Media;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Lists;

/// <inheritdoc cref="IListStore"/>
public sealed class ListStore(MovieDbContext database, ICurrentUser currentUser) : IListStore
{
    /// <summary>
    /// How many times a colliding join code is regenerated before giving up.
    /// </summary>
    /// <remarks>
    /// Eight symbols from an alphabet of 32 is a space of about 1.1 trillion,
    /// so a collision is not something anyone will meet. The retry is here so
    /// that if one ever happens it costs a second round trip rather than a
    /// failed request.
    /// </remarks>
    private const int JoinCodeAttempts = 3;

    public async Task<IReadOnlyList<MediaList>> MineAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return [];
        }

        return await database.ListMembers
            .Where(membership => membership.UserId == userId
                && membership.Status == MemberStatus.Accepted)
            .Select(membership => membership.List!)
            .OrderByDescending(list => list.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<MediaList> CreateAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id
            ?? throw new InvalidOperationException("A list cannot be created by nobody.");

        for (var attempt = 1; ; attempt++)
        {
            var list = new MediaList { Name = name, CreatedById = userId };

            database.Lists.Add(list);

            // In the same SaveChanges, so it is one transaction. A list whose
            // creator is not a member of it would be one nobody could read.
            database.ListMembers.Add(new ListMember
            {
                ListId = list.Id,
                UserId = userId,
                Role = MemberRole.Owner,
                Status = MemberStatus.Accepted,
                RespondedAt = DateTime.UtcNow,
            });

            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return list;
            }
            catch (DbUpdateException e)
                when (attempt < JoinCodeAttempts && UniqueViolations.Caused(e))
            {
                // The only unique index this write can break is the join code's,
                // since the list and its membership are both brand new.
                database.ForgetPendingInserts<MediaList>();
                database.ForgetPendingInserts<ListMember>();
            }
        }
    }

    public async Task RenameAsync(
        MediaList list,
        string name,
        CancellationToken cancellationToken = default)
    {
        list.Name = name;

        // Renaming is open to every member, which is exactly why the creator
        // column is guarded in SaveChanges: without that, a rename could hand
        // the list over in the same statement.
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(MediaList list, CancellationToken cancellationToken = default)
    {
        database.Lists.Remove(list);

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ListMember>> MembersAsync(
        MediaList list,
        CancellationToken cancellationToken = default) =>
        await database.ListMembers
            .Where(membership => membership.ListId == list.Id)
            .Include(membership => membership.User)
            .OrderBy(membership => membership.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task RemoveMemberAsync(
        ListMember membership,
        CancellationToken cancellationToken = default)
    {
        database.ListMembers.Remove(membership);

        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ListItem>> ItemsAsync(
        MediaList list,
        CancellationToken cancellationToken = default) =>
        await database.ListItems
            .Where(item => item.ListId == list.Id)
            .Include(item => item.AddedBy)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<ListItem> AddItemAsync(
        MediaList list,
        TitleSnapshot title,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id
            ?? throw new InvalidOperationException("An item cannot be added by nobody.");

        var item = new ListItem
        {
            ListId = list.Id,
            MediaId = title.MediaId,
            MediaType = title.MediaType,
            Title = title.Title,
            PosterPath = title.PosterPath,
            VoteAverage = title.VoteAverage,
            Year = title.Year,
            Genres = title.Genres,
            AddedById = userId,
        };

        database.ListItems.Add(item);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (UniqueViolations.Caused(e))
        {
            // Somebody got there first — possibly in the second between this
            // caller opening the screen and tapping add. The list holds the
            // title either way, so the row already there is the answer.
            database.ForgetPendingInserts<ListItem>();

            return await ExistingAsync(list, title.MediaId, title.MediaType, cancellationToken);
        }

        return await ExistingAsync(list, title.MediaId, title.MediaType, cancellationToken);
    }

    public async Task<bool> RemoveItemAsync(
        MediaList list,
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default) =>
        await database.ListItems
            .Where(item => item.ListId == list.Id
                && item.MediaId == mediaId
                && item.MediaType == mediaType)
            .ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>
    /// Re-read so the adder's profile comes with it, which is what the members
    /// badge on the item is drawn from.
    /// </summary>
    private async Task<ListItem> ExistingAsync(
        MediaList list,
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken) =>
        await database.ListItems
            .Include(item => item.AddedBy)
            .FirstAsync(
                item => item.ListId == list.Id
                    && item.MediaId == mediaId
                    && item.MediaType == mediaType,
                cancellationToken);
}
