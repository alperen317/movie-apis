using Microsoft.EntityFrameworkCore;

using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Library;
using Movie.Domain.Library;
using Movie.Domain.Media;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Library;

/// <inheritdoc cref="IRecommendationFeedbackStore"/>
public sealed class RecommendationFeedbackStore(MovieDbContext context, ICurrentUser currentUser)
    : IRecommendationFeedbackStore
{
    public async Task<IReadOnlyList<RecommendationFeedback>> ListAsync(
        CancellationToken cancellationToken = default) =>
        await context.RecommendationFeedback.ToListAsync(cancellationToken);

    public async Task<bool> DismissAsync(
        int mediaId,
        MediaType mediaType,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.Id is not { } userId)
        {
            return false;
        }

        context.RecommendationFeedback.Add(new RecommendationFeedback
        {
            UserId = userId,
            MediaId = mediaId,
            MediaType = mediaType,
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException e) when (UniqueViolations.Caused(e))
        {
            // Already dismissed. Written first and caught here rather than
            // checked first, because the check would only narrow the race, and
            // the answer either way is that the title stays hidden.
            context.ForgetPendingInserts<RecommendationFeedback>();
            return false;
        }
    }
}