using System.Security.Claims;

using Mediator;

using Microsoft.IdentityModel.JsonWebTokens;

using Movie.Application.Features.Account;
using Movie.Domain.Users;

namespace Movie.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").RequireAuthorization().WithTags("Account");

        group.MapGet("/", GetProfile);
        group.MapPut("/", UpdateProfile);
        group.MapDelete("/", DeleteAccount);
    }

    private static async Task<IResult> GetProfile(ClaimsPrincipal principal, ISender sender)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var profile = await sender.Send(new GetProfileQuery(userId));

        // A token outlives the account it names by up to an hour, so a valid
        // signature is not proof the user still exists.
        return profile is null ? Results.Unauthorized() : Results.Ok(profile);
    }

    /// <summary>
    /// Replaces the editable fields wholesale — an omitted field is stored as
    /// absent, not left alone. See <see cref="UpdateProfileCommand"/>.
    /// </summary>
    private static async Task<IResult> UpdateProfile(
        UpdateProfileRequest request,
        ClaimsPrincipal principal,
        ISender sender)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        if (request.DisplayName is { Length: > 60 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["displayName"] = ["Display name cannot be longer than 60 characters."],
            });
        }

        if (request.WatchRegion is { Length: > 0 and not 2 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["watchRegion"] = ["Watch region must be a two-letter country code."],
            });
        }

        var profile = await sender.Send(new UpdateProfileCommand(
            userId,
            request.DisplayName,
            request.AvatarVariant,
            request.AvatarSeed,
            request.WatchRegion));

        return profile is null ? Results.Unauthorized() : Results.Ok(profile);
    }

    private static async Task<IResult> DeleteAccount(ClaimsPrincipal principal, ISender sender)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var deleted = await sender.Send(new DeleteAccountCommand(userId));

        // Already gone counts as done, so a retried request does not fail.
        return deleted ? Results.NoContent() : Results.Unauthorized();
    }

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out userId);

    public sealed record UpdateProfileRequest(
        string? DisplayName,
        AvatarVariant AvatarVariant,
        string? AvatarSeed,
        string? WatchRegion);
}