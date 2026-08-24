using Mediator;
using Movie.Application.Abstractions.Lists;

namespace Movie.Application.Features.Lists;

/// <summary>
/// Renames a list. Open to every member rather than the creator alone: a shared
/// list is shared, and naming it is part of using it.
/// </summary>
public sealed record RenameListCommand(Guid ListId, string Name) : IRequest<SharedListDto?>;

public sealed class RenameListCommandHandler(
    IListAccess access,
    IListStore lists,
    IListEventPublisher events)
    : IRequestHandler<RenameListCommand, SharedListDto?>
{
    /// <returns>Null when the caller has no such list to rename.</returns>
    public async ValueTask<SharedListDto?> Handle(
        RenameListCommand command,
        CancellationToken cancellationToken)
    {
        var list = await access.ForMemberAsync(command.ListId, cancellationToken);

        if (list is null)
        {
            return null;
        }

        await lists.RenameAsync(list, command.Name, cancellationToken);

        await events.ListRenamedAsync(list.Id, command.Name, cancellationToken);

        return SharedListDto.ForMember(list);
    }
}
