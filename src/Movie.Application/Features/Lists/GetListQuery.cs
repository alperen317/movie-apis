using Mediator;

using Movie.Application.Abstractions.Lists;

namespace Movie.Application.Features.Lists;

/// <summary>
/// One list, as much of it as the caller is entitled to.
/// </summary>
public sealed record GetListQuery(Guid ListId) : IRequest<SharedListDto?>;

public sealed class GetListQueryHandler(IListAccess access)
    : IRequestHandler<GetListQuery, SharedListDto?>
{
    public async ValueTask<SharedListDto?> Handle(
        GetListQuery query,
        CancellationToken cancellationToken)
    {
        // Asked as a member first, because passing that is what distinguishes
        // somebody who joined from somebody still deciding — and that is what
        // decides whether the join code comes back.
        var joined = await access.ForMemberAsync(query.ListId, cancellationToken);

        if (joined is not null)
        {
            return SharedListDto.ForMember(joined);
        }

        var invited = await access.ForViewerAsync(query.ListId, cancellationToken);

        return invited is null ? null : SharedListDto.ForInvitee(invited);
    }
}