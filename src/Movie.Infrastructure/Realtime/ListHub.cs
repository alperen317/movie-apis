using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;

using Movie.Application.Abstractions.Lists;

namespace Movie.Infrastructure.Realtime;

/// <summary>
/// The realtime counterpart of a shared list. One group per list,
/// <c>list:{listId}</c>, matching the plan's naming rather than SignalR's own
/// group-per-connection default.
/// </summary>
/// <remarks>
/// A hub method is the only door in: nothing joins a group without asking, and
/// the ask goes through <see cref="IListAccess"/> exactly like a handler would.
/// Skipping that check here would reopen the hole Faz 3 closed on the HTTP
/// side — anyone could listen in on any list's changes just by knowing its id.
/// </remarks>
[Authorize]
public sealed class ListHub(IListAccess access, UserConnectionTracker connections) : Hub<IListHubClient>
{
    public override Task OnConnectedAsync()
    {
        if (TryGetUserId(out var userId))
        {
            connections.Add(userId, Context.ConnectionId);
        }

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (TryGetUserId(out var userId))
        {
            connections.Remove(userId, Context.ConnectionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    public async Task JoinList(Guid listId)
    {
        if (await access.ForMemberAsync(listId) is null)
        {
            // Same non-answer as the REST endpoints: a list the caller is not
            // in and a list that does not exist look identical from here.
            throw new HubException("not_a_member");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(listId));
    }

    public Task LeaveList(Guid listId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(listId));

    public static string GroupName(Guid listId) => $"list:{listId}";

    // Context.User rather than ICurrentUser: HttpContextPropagationHubFilter
    // only wraps a hub method invocation, not the connect/disconnect
    // lifecycle, so IHttpContextAccessor.HttpContext -- and anything built on
    // it -- is unavailable here.
    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);
}