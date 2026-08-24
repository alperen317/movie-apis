using Mediator;

using Microsoft.AspNetCore.Identity;

using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

public sealed record LoginCommand(string Email, string Password) : IRequest<LoginResult>;

public enum LoginFailure
{
    InvalidCredentials,

    /// <summary>The account exists and the password was right, but sign-up was never finished.</summary>
    EmailNotConfirmed,

    LockedOut,
}

public sealed record LoginResult(AuthTokens? Tokens, LoginFailure? Failure)
{
    public static LoginResult Failed(LoginFailure failure) => new(null, failure);

    public static LoginResult Succeeded(AuthTokens tokens) => new(tokens, null);
}

public sealed class LoginCommandHandler(
    UserManager<ApplicationUser> users,
    AuthTokenIssuer tokens) : IRequestHandler<LoginCommand, LoginResult>
{
    public async ValueTask<LoginResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());

        if (user is null)
        {
            return LoginResult.Failed(LoginFailure.InvalidCredentials);
        }

        // Checked before the password, so a locked account cannot be used to go
        // on testing passwords — which is the whole point of locking it. The
        // cost is that reaching this state confirms the account exists, but
        // getting here already takes ten failed attempts against the rate
        // limiter, so it is a poor way to go looking for accounts.
        if (await users.IsLockedOutAsync(user))
        {
            return LoginResult.Failed(LoginFailure.LockedOut);
        }

        if (!await users.CheckPasswordAsync(user, command.Password))
        {
            await users.AccessFailedAsync(user);
            return LoginResult.Failed(LoginFailure.InvalidCredentials);
        }

        // Only reported once the password is known to be right. Reported any
        // earlier, it would answer "does this address have an account" for
        // anyone who asked.
        if (!user.EmailConfirmed)
        {
            return LoginResult.Failed(LoginFailure.EmailNotConfirmed);
        }

        await users.ResetAccessFailedCountAsync(user);

        return LoginResult.Succeeded(await tokens.IssueAsync(user, cancellationToken));
    }
}