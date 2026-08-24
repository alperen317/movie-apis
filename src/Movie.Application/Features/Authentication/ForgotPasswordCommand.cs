using Mediator;

using Microsoft.AspNetCore.Identity;

using Movie.Application.Abstractions.Authentication;
using Movie.Application.Abstractions.Email;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

/// <summary>
/// Sends a reset code. Returns nothing on purpose: the caller must not be able
/// to tell an unknown address from one with an account.
/// </summary>
public sealed record ForgotPasswordCommand(string Email) : IRequest;

public sealed class ForgotPasswordCommandHandler(
    UserManager<ApplicationUser> users,
    IVerificationCodeService codes,
    IVerificationEmailSender emails) : IRequestHandler<ForgotPasswordCommand>
{
    public async ValueTask<Unit> Handle(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());

        // Nothing goes out for an unknown address, and nothing for one that
        // never finished sign-up either — there is no password to reset yet,
        // and sending would turn this into a way to mail people on demand.
        if (user is not null && user.EmailConfirmed)
        {
            var code = await codes.IssueAsync(user, CodePurpose.PasswordReset, cancellationToken);
            await emails.SendAsync(user, CodePurpose.PasswordReset, code, cancellationToken);
        }

        return Unit.Value;
    }
}