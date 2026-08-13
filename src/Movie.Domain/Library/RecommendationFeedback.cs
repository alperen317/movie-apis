using Movie.Domain.Media;

namespace Movie.Domain.Library;

/// <summary>
/// A "not interested" signal. Dismissed titles are excluded from every
/// personalized rail.
/// </summary>
/// <remarks>
/// Deliberately does not use <see cref="MediaSnapshot"/>: this row is an
/// exclusion filter, not something ever rendered, so it needs no title, poster
/// or genres. The id and kind are enough.
/// </remarks>
public sealed class RecommendationFeedback
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required int MediaId { get; init; }

    public required MediaType MediaType { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
