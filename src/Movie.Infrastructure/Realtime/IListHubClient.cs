using Movie.Application.Features.Lists;
using Movie.Domain.Media;

namespace Movie.Infrastructure.Realtime;

/// <summary>
/// The messages a list's group can receive. Kept as an interface so
/// <c>IHubContext&lt;ListHub, IListHubClient&gt;</c> gives callers compile-time
/// checked method names instead of the untyped <c>SendAsync("MethodName", ...)</c>
/// SignalR falls back to without one.
/// </summary>
public interface IListHubClient
{
    Task ItemAdded(ListItemDto item);

    Task ItemRemoved(ItemRemovedPayload payload);

    Task MembersChanged();

    Task ListRenamed(ListRenamedPayload payload);

    Task ListDeleted();

    Task PollUpdated();
}

public sealed record ItemRemovedPayload(int MediaId, MediaType MediaType);

public sealed record ListRenamedPayload(string Name);