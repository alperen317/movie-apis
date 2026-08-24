using Microsoft.AspNetCore.SignalR;

using Movie.Application.Abstractions.Lists;
using Movie.Application.Features.Lists;
using Movie.Domain.Media;

namespace Movie.Infrastructure.Realtime;

public sealed class SignalRListEventPublisher(IHubContext<ListHub, IListHubClient> hub)
    : IListEventPublisher
{
    public Task ItemAddedAsync(
        Guid listId,
        ListItemDto item,
        CancellationToken cancellationToken = default) =>
        Group(listId).ItemAdded(item);

    public Task ItemRemovedAsync(
        Guid listId,
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default) =>
        Group(listId).ItemRemoved(new ItemRemovedPayload(mediaId, mediaType));

    public Task MembersChangedAsync(Guid listId, CancellationToken cancellationToken = default) =>
        Group(listId).MembersChanged();

    public Task ListRenamedAsync(
        Guid listId,
        string name,
        CancellationToken cancellationToken = default) =>
        Group(listId).ListRenamed(new ListRenamedPayload(name));

    public Task ListDeletedAsync(Guid listId, CancellationToken cancellationToken = default) =>
        Group(listId).ListDeleted();

    public Task PollUpdatedAsync(Guid listId, CancellationToken cancellationToken = default) =>
        Group(listId).PollUpdated();

    private IListHubClient Group(Guid listId) => hub.Clients.Group(ListHub.GroupName(listId));
}