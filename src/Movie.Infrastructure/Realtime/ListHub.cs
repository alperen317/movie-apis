using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

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
public sealed class ListHub(IListAccess access) : Hub<IListHubClient>
{
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
}