using Mediator;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Media;

namespace Movie.Application.Features.Lists;

/// <summary>
/// How many of a list's members have already seen each of its titles.
/// </summary>
/// <remarks>
/// The only query in the application that reads across users, and it reports a
/// number per title and nothing else — see
/// <see cref="IListStore.WatchSummaryAsync"/>.
/// </remarks>
public sealed record GetWatchSummaryQuery(Guid ListId)
    : IRequest<IReadOnlyList<WatchSummaryDto>?>;

/// <param name="WatchedCount">
/// Distinct members. A title nobody has seen is not in the result at all,
/// rather than present with a zero.
/// </param>
public sealed record WatchSummaryDto(int MediaId, MediaType MediaType, int WatchedCount);

public sealed class GetWatchSummaryQueryHandler(IListAccess access, IListStore lists)
    : IRequestHandler<GetWatchSummaryQuery, IReadOnlyList<WatchSummaryDto>?>
{
    /// <returns>Null when the caller is not a member of the list.</returns>
    public async ValueTask<IReadOnlyList<WatchSummaryDto>?> Handle(
        GetWatchSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var list = await access.ForMemberAsync(query.ListId, cancellationToken);

        if (list is null)
        {
            return null;
        }

        var summary = await lists.WatchSummaryAsync(list, cancellationToken);

        return [.. summary.Select(x => new WatchSummaryDto(x.MediaId, x.MediaType, x.Count))];
    }
}
