using Mediator;

using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;
using Movie.Domain.Media;
using Movie.Domain.Users;

namespace Movie.Application.Features.Lists;

/// <summary>
/// What is on a list, newest addition first.
/// </summary>
public sealed record GetListItemsQuery(Guid ListId) : IRequest<IReadOnlyList<ListItemDto>?>;

/// <param name="RowId">
/// The list_items row. Needed because a poll's candidates point at items rather
/// than at titles, so an item has to be nameable on its own.
/// </param>
/// <param name="Id">TMDB's id for the title.</param>
/// <param name="AddedByName">
/// Display name, falling back to the address. Shown on the badge that says who
/// put the title on the list; it confers nothing, since any member may remove
/// any item.
/// </param>
public sealed record ListItemDto(
    Guid RowId,
    Guid ListId,
    int Id,
    MediaType MediaType,
    string Title,
    string? PosterPath,
    decimal? VoteAverage,
    string? Year,
    string[] Genres,
    Guid AddedBy,
    string AddedByName,
    AvatarVariant AddedByAvatarVariant,
    string? AddedByAvatarSeed,
    DateTime AddedAt)
{
    public static ListItemDto From(ListItem item) => new(
        item.Id,
        item.ListId,
        item.MediaId,
        item.MediaType,
        item.Title,
        item.PosterPath,
        item.VoteAverage,
        item.Year,
        item.Genres,
        item.AddedById,
        Name(item.AddedBy),
        item.AddedBy?.AvatarVariant ?? AvatarVariant.Beam,
        item.AddedBy?.AvatarSeed,
        item.CreatedAt);

    private static string Name(ApplicationUser? user) =>
        string.IsNullOrWhiteSpace(user?.DisplayName)
            ? user?.Email ?? string.Empty
            : user.DisplayName;
}

public sealed class GetListItemsQueryHandler(IListAccess access, IListStore lists)
    : IRequestHandler<GetListItemsQuery, IReadOnlyList<ListItemDto>?>
{
    /// <returns>Null when the caller is not a member of the list.</returns>
    public async ValueTask<IReadOnlyList<ListItemDto>?> Handle(
        GetListItemsQuery query,
        CancellationToken cancellationToken)
    {
        var list = await access.ForMemberAsync(query.ListId, cancellationToken);

        if (list is null)
        {
            return null;
        }

        var items = await lists.ItemsAsync(list, cancellationToken);

        return [.. items.Select(ListItemDto.From)];
    }
}

/// <summary>
/// Puts a title on a list.
/// </summary>
public sealed record AddListItemCommand(Guid ListId, TitleSnapshot Title) : IRequest<ListItemDto?>;

public sealed class AddListItemCommandHandler(
    IListAccess access,
    IListStore lists,
    IListEventPublisher events)
    : IRequestHandler<AddListItemCommand, ListItemDto?>
{
    /// <returns>
    /// The item, whether it was just added or was already there. Null when the
    /// caller is not a member of the list.
    /// </returns>
    public async ValueTask<ListItemDto?> Handle(
        AddListItemCommand command,
        CancellationToken cancellationToken)
    {
        var list = await access.ForMemberAsync(command.ListId, cancellationToken);

        if (list is null)
        {
            return null;
        }

        var item = await lists.AddItemAsync(list, command.Title, cancellationToken);
        var dto = ListItemDto.From(item);

        // Sent even when the item was already there: a co-member's client that
        // does not yet know that still benefits from hearing it.
        await events.ItemAddedAsync(list.Id, dto, cancellationToken);

        return dto;
    }
}

/// <summary>
/// Takes a title off a list. Any accepted member may, regardless of who added
/// it — see <see cref="IListStore.RemoveItemAsync"/>.
/// </summary>
public sealed record RemoveListItemCommand(Guid ListId, int MediaId, MediaType MediaType)
    : IRequest<bool>;

public sealed class RemoveListItemCommandHandler(
    IListAccess access,
    IListStore lists,
    IListEventPublisher events)
    : IRequestHandler<RemoveListItemCommand, bool>
{
    /// <returns>Whether the caller is a member of the list at all.</returns>
    public async ValueTask<bool> Handle(
        RemoveListItemCommand command,
        CancellationToken cancellationToken)
    {
        var list = await access.ForMemberAsync(command.ListId, cancellationToken);

        if (list is null)
        {
            return false;
        }

        // Whether a row actually went is not passed on. A title somebody else
        // removed a moment ago leaves the list in the state the caller asked
        // for, which is not a failure.
        await lists.RemoveItemAsync(list, command.MediaId, command.MediaType, cancellationToken);

        await events.ItemRemovedAsync(
            list.Id,
            command.MediaId,
            command.MediaType,
            cancellationToken);

        return true;
    }
}