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

public sealed class DeleteListCommandHandler(IListAccess access, IListStore lists)
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

        await lists.DeleteAsync(list, cancellationToken);

        return true;
    }
}
