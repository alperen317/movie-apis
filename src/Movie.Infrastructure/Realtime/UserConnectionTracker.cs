namespace Movie.Infrastructure.Realtime;

/// <summary>
/// Which SignalR connections belong to which signed-in user. The one thing
/// group membership alone cannot answer — removing a connection from a group
/// needs a connection id, not a user id — so a handler that needs to evict a
/// specific user from a list's group (see
/// <see cref="Movie.Application.Abstractions.Lists.IListEventPublisher.MemberEvictedAsync"/>)
/// looks it up here.
/// </summary>
/// <remarks>
/// A single coarse lock rather than a lock-free <c>ConcurrentDictionary</c> of
/// dictionaries: connect/disconnect events are infrequent enough that the
/// contention cost is nothing, and a lock-free remove-if-empty on a nested
/// dictionary has a real race — a connection added between the emptiness
/// check and the outer removal gets silently orphaned. A reconnecting client
/// (SignalR's own auto-reconnect included) hits exactly that window.
/// </remarks>
public sealed class UserConnectionTracker
{
    private readonly Dictionary<Guid, HashSet<string>> connectionsByUser = [];
    private readonly Lock gate = new();

    public void Add(Guid userId, string connectionId)
    {
        lock (gate)
        {
            if (!connectionsByUser.TryGetValue(userId, out var connections))
            {
                connections = [];
                connectionsByUser[userId] = connections;
            }

            connections.Add(connectionId);
        }
    }

    public void Remove(Guid userId, string connectionId)
    {
        lock (gate)
        {
            if (!connectionsByUser.TryGetValue(userId, out var connections))
            {
                return;
            }

            connections.Remove(connectionId);

            if (connections.Count == 0)
            {
                connectionsByUser.Remove(userId);
            }
        }
    }

    public IReadOnlyCollection<string> ConnectionsFor(Guid userId)
    {
        lock (gate)
        {
            return connectionsByUser.TryGetValue(userId, out var connections) ? [.. connections] : [];
        }
    }
}