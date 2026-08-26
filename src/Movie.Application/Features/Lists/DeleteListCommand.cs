using Mediator;

using Movie.Application.Abstractions.Lists;

namespace Movie.Application.Features.Lists;

/// <summary>
/// Deletes a list for everybody in it, along with its members, items and polls.
/// </summary>
/// <remarks>
/// The creator's alone. This is the one operation that takes something away
/// from people other than the person asking, which is why renaming is open to
/// all members and this is not.
/// </remarks>
public sealed record DeleteListCommand(Guid ListId) : IRequest<bool>;

public sealed class DeleteListCommandHandler(
    IListAccess access,
    IListStore lists,
    IListEventPublisher events)
    : IRequestHandler<DeleteListCommand, bool>
{
    public async ValueTask<bool> Handle(
        DeleteListCommand command,
        CancellationToken cancellationToken)
    {
        var list = await access.ForOwnerAsync(command.ListId, cancellationToken);

        if (list is null)
        {
            return false;
        }

        // Broadcast first: once the row is gone, IListAccess has nothing left
        // to check a rejoin against, and a member still connected should hear
        // about the deletion rather than just stop hearing about anything else.
        await events.ListDeletedAsync(list.Id, cancellationToken);

        // Read the roster before the row goes away -- ListMembers cascades
        // with it -- but evict only after the delete actually commits. Doing
        // it the other way around would mean a failure in the eviction loop
        // (a transient error, one connection among many) leaves some members
        // forced out of a group for a list that, having never reached
        // DeleteAsync, still exists.
        var members = await lists.MembersAsync(list, cancellationToken);

        await lists.DeleteAsync(list, cancellationToken);

        foreach (var member in members)
        {
            await events.MemberEvictedAsync(list.Id, member.UserId, cancellationToken);
        }

        return true;
    }
}