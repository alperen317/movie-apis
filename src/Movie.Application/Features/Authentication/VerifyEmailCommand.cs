using Mediator;
using Microsoft.AspNetCore.Identity;
using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

public sealed record VerifyEmailCommand(string Email, string Code) : IRequest<VerifyEmailResult>;

/// <param name="Tokens">
/// Present only on success. Confirming the address signs the user in, matching
/// what Supabase did, so the client does not have to ask for a password it just
/// collected during sign-up.
/// </param>
public sealed record VerifyEmailResult(VerificationResult Outcome, AuthTokens? Tokens)
{
    public static VerifyEmailResult Failed(VerificationResult outcome) => new(outcome, null);
}

public sealed class VerifyEmailCommandHandler(
    UserManager<ApplicationUser> users,
    IVerificationCodeService codes,
    AuthTokenIssuer tokens) : IRequestHandler<VerifyEmailCommand, VerifyEmailResult>
{
    public async ValueTask<VerifyEmailResult> Handle(
        VerifyEmailCommand command,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());

        // An unknown address is answered exactly as a wrong code would be.
        if (user is null || user.EmailConfirmed)
        {
            return VerifyEmailResult.Failed(VerificationResult.Invalid);
        }

        var result = await codes.ConsumeAsync(
            user,
            CodePurpose.EmailConfirmation,
            command.Code.Trim(),
            cancellationToken);

        if (result != VerificationResult.Success)
        {
            return VerifyEmailResult.Failed(result);
        }

        // Redeeming the code and acting on it happen in the same request. Split
        // across two, the code would stay live in between and could be replayed
        // by anyone who saw it.
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);

        return new VerifyEmailResult(
            VerificationResult.Success,
            await tokens.IssueAsync(user, cancellationToken));
    }
}
