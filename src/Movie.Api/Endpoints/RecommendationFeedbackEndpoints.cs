using Mediator;
using Movie.Application.Features.Library;
using Movie.Domain.Media;

namespace Movie.Api.Endpoints;

/// <summary>
/// "Not interested." Dismissed titles are excluded from every personalized
/// rail.
/// </summary>
public static class RecommendationFeedbackEndpoints
{
    public static void MapRecommendationFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/recommendation-feedback")
            .RequireAuthorization()
            .WithTags("Library");

        group.MapGet("/", GetDismissed);
        group.MapPost("/", Dismiss);
    }

    private static async Task<IResult> GetDismissed(ISender sender) =>
        Results.Ok(await sender.Send(new GetDismissedRecommendationsQuery()));

    private static async Task<IResult> Dismiss(DismissRequest request, ISender sender)
    {
        await sender.Send(new DismissRecommendationCommand(request.MediaId, request.MediaType));

        // Dismissing a title already dismissed is not an error: what the caller
        // asked for — that it stays hidden — already holds.
        return Results.NoContent();
    }

    public sealed record DismissRequest(int MediaId, MediaType MediaType);
}
