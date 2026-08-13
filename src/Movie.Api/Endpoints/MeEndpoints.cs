using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Movie.Api.Endpoints;

public static class MeEndpoints
{
    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/me").RequireAuthorization().WithTags("Account");

        // Reads straight from the token for now. Phase 2e replaces this with
        // the stored profile and adds the update and delete operations.
        group.MapGet("/", (ClaimsPrincipal principal) => Results.Ok(new
        {
            id = principal.FindFirstValue(JwtRegisteredClaimNames.Sub),
            email = principal.FindFirstValue(JwtRegisteredClaimNames.Email),
        }));
    }
}
