using Mediator;

using Movie.Application.Abstractions.Library;
using Movie.Domain.Media;

namespace Movie.Application.Features.Library;

/// <summary>
/// Every title the caller has dismissed. Returned whole rather than filtered
/// server-side: the client holds these as a set and applies them to rails it
/// has already fetched, so there is nothing to page through.
/// </summary>
public sealed record GetDismissedRecommendationsQuery
    : IRequest<IReadOnlyList<DismissedMediaDto>>;

public sealed record DismissedMediaDto(int MediaId, MediaType MediaType);

public sealed class GetDismissedRecommendationsQueryHandler(IRecommendationFeedbackStore dismissals)
    : IRequestHandler<GetDismissedRecommendationsQuery, IReadOnlyList<DismissedMediaDto>>
{
    public async ValueTask<IReadOnlyList<DismissedMediaDto>> Handle(
        GetDismissedRecommendationsQuery query,
        CancellationToken cancellationToken)
    {
        var rows = await dismissals.ListAsync(cancellationToken);

        return [.. rows.Select(x => new DismissedMediaDto(x.MediaId, x.MediaType))];
    }
}