using Mediator;

using Movie.Application.Abstractions.Library;
using Movie.Domain.Media;

namespace Movie.Application.Features.Library;

/// <summary>
/// "Not interested." The title stops appearing in personalized rails.
/// </summary>
/// <remarks>
/// Carries no title or poster: the row is an exclusion filter and is never
/// rendered, so the id and kind are all of it. See
/// <c>RecommendationFeedback</c>.
/// </remarks>
public sealed record DismissRecommendationCommand(int MediaId, MediaType MediaType)
    : IRequest<bool>;

public sealed class DismissRecommendationCommandHandler(IRecommendationFeedbackStore dismissals)
    : IRequestHandler<DismissRecommendationCommand, bool>
{
    /// <returns>Whether this was a new dismissal rather than a repeat.</returns>
    public ValueTask<bool> Handle(
        DismissRecommendationCommand command,
        CancellationToken cancellationToken) =>
        new(dismissals.DismissAsync(command.MediaId, command.MediaType, cancellationToken));
}