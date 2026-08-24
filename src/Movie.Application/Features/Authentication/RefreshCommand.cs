using Mediator;

using Microsoft.AspNetCore.Identity;

using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

public sealed record RefreshCommand(string RefreshToken) : IRequest<AuthTokens?>;

public sealed class RefreshCommandHandler(
    UserManager<ApplicationUser> users,
    IAccessTokenGenerator accessTokens,
    IRefreshTokenService refreshTokens) : IRequestHandler<RefreshCommand, AuthTokens?>
{
    public async ValueTask<AuthTokens?> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        var outcome = await refreshTokens.RotateAsync(command.RefreshToken, cancellationToken);

        if (!outcome.Succeeded)
        {
            return null;
        }

        var user = await users.FindByIdAsync(outcome.UserId!.Value.ToString());

        // The row survives only as long as the user does, so this is the narrow
        // case of an account deleted between the two statements.
        if (user is null)
        {
            return null;
        }

        var access = accessTokens.Generate(user);

        return new AuthTokens(access.Value, access.ExpiresAtUtc, outcome.RefreshToken!);
    }
}