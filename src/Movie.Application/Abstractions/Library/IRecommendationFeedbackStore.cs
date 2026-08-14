using Movie.Domain.Library;
using Movie.Domain.Media;

namespace Movie.Application.Abstractions.Library;

/// <summary>
/// The titles the caller has said they are not interested in.
/// </summary>
public interface IRecommendationFeedbackStore
{
    /// <summary>
    /// Every dismissal. The client turns these into a lookup set and filters
    /// its recommendation rails against it, so the whole list is what it wants.
    /// </summary>
    Task<IReadOnlyList<RecommendationFeedback>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this was a new dismissal. Dismissing the same title twice is a
    /// no-op, not an error.
    /// </summary>
    Task<bool> DismissAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default);
}
