using Mediator;

using Movie.Application.Abstractions.Lists;

namespace Movie.Application.Features.Lists;

/// <summary>
/// The lists the caller has joined. Invitations they have not answered are not
/// among them — those are their own screen.
/// </summary>
public sealed record GetMyListsQuery : IRequest<IReadOnlyList<SharedListDto>>;

public sealed class GetMyListsQueryHandler(IListStore lists)
    : IRequestHandler<GetMyListsQuery, IReadOnlyList<SharedListDto>>
{
    public async ValueTask<IReadOnlyList<SharedListDto>> Handle(
        GetMyListsQuery query,
        CancellationToken cancellationToken)
    {
        var mine = await lists.MineAsync(cancellationToken);

        // Every one of these is a list the caller accepted, so the join code
        // goes with it.
        return [.. mine.Select(SharedListDto.ForMember)];
    }
}