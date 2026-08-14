using Mediator;
using Movie.Application.Abstractions.Lists;

namespace Movie.Application.Features.Lists;

/// <summary>
/// Starts a shared list. Open to anyone signed in — there is nothing yet to be
/// a member of.
/// </summary>
public sealed record CreateListCommand(string Name) : IRequest<SharedListDto>;

public sealed class CreateListCommandHandler(IListStore lists)
    : IRequestHandler<CreateListCommand, SharedListDto>
{
    public async ValueTask<SharedListDto> Handle(
        CreateListCommand command,
        CancellationToken cancellationToken)
    {
        var list = await lists.CreateAsync(command.Name, cancellationToken);

        // The creator is a member by definition, so they get the whole thing
        // including the code they will share to bring people in.
        return SharedListDto.ForMember(list);
    }
}
