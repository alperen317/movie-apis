using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

/// <summary>
/// Mints the pair a signed-in client needs. Shared by sign-in, refresh and the
/// end of the sign-up flow, so all three hand back tokens of the same shape.
/// </summary>
public sealed class AuthTokenIssuer(
    IAccessTokenGenerator accessTokens,
    IRefreshTokenService refreshTokens)
{
    public async Task<AuthTokens> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var access = accessTokens.Generate(user);
        var refresh = await refreshTokens.IssueAsync(user, cancellationToken);

        return new AuthTokens(access.Value, access.ExpiresAtUtc, refresh);
    }
}
