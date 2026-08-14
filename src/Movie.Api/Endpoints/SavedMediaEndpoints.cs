using Mediator;
using Movie.Application.Features.Library;
using Movie.Domain.Library;
using Movie.Domain.Media;
using static Movie.Api.Endpoints.EnumParameters;

namespace Movie.Api.Endpoints;

/// <summary>
/// Favorites and the watchlist.
/// </summary>
/// <remarks>
/// Nothing here takes a user id. The context's ownership filter scopes every
/// read and delete to whoever the token names, and the store stamps the owner
/// on every write, so there is no parameter that could name someone else.
/// </remarks>
public static class SavedMediaEndpoints
{
    public static void MapSavedMediaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/saved-media").RequireAuthorization().WithTags("Library");

        group.MapGet("/", GetSavedMedia);
        group.MapPost("/", SaveMedia);
        group.MapPost("/batch", SaveMediaBatch);
        group.MapDelete("/{mediaType}/{mediaId:int}", RemoveSavedMedia);
    }

    private static async Task<IResult> GetSavedMedia(string listType, ISender sender)
    {
        if (!TryParse<ListType>(listType, out var list))
        {
            return NotOneOf<ListType>(nameof(listType));
        }

        return Results.Ok(await sender.Send(new GetSavedMediaQuery(list)));
    }

    /// <summary>
    /// Saving a title that is already saved is not an error — see
    /// <see cref="Application.Abstractions.Library.ISavedMediaStore.SaveAsync"/>.
    /// The count in the response is how the caller learns which of the two
    /// happened.
    /// </summary>
    private static async Task<IResult> SaveMedia(
        string listType,
        TitleRequest request,
        ISender sender)
    {
        if (!TryParse<ListType>(listType, out var list))
        {
            return NotOneOf<ListType>(nameof(listType));
        }

        if (request.Validate() is { } problem)
        {
            return problem;
        }

        var saved = await sender.Send(new SaveMediaCommand([request.ToSnapshot()], list));

        return Results.Ok(new SaveMediaResponse(saved));
    }

    /// <summary>For the TV Time / Letterboxd importer.</summary>
    private static async Task<IResult> SaveMediaBatch(
        string listType,
        TitleRequest[] request,
        ISender sender)
    {
        if (!TryParse<ListType>(listType, out var list))
        {
            return NotOneOf<ListType>(nameof(listType));
        }

        if (request.Length > Batches.MaxTitles)
        {
            return Batches.TooLarge(Batches.MaxTitles, "titles");
        }

        for (var i = 0; i < request.Length; i++)
        {
            if (request[i].Validate(i) is { } problem)
            {
                return problem;
            }
        }

        var saved = await sender.Send(
            new SaveMediaCommand([.. request.Select(x => x.ToSnapshot())], list));

        return Results.Ok(new SaveMediaResponse(saved));
    }

    private static async Task<IResult> RemoveSavedMedia(
        string mediaType,
        int mediaId,
        string listType,
        ISender sender)
    {
        if (!TryParse<MediaType>(mediaType, out var kind))
        {
            return NotOneOf<MediaType>(nameof(mediaType));
        }

        if (!TryParse<ListType>(listType, out var list))
        {
            return NotOneOf<ListType>(nameof(listType));
        }

        await sender.Send(new RemoveSavedMediaCommand(mediaId, kind, list));

        // Nothing to remove counts as removed, so a retried request does not
        // fail and the caller's end state is the same either way.
        return Results.NoContent();
    }

    /// <param name="Saved">
    /// How many titles were not saved already. Zero means the caller's local
    /// state was behind, which is worth knowing and is not a failure.
    /// </param>
    public sealed record SaveMediaResponse(int Saved);
}
