using Mediator;
using Microsoft.AspNetCore.Identity;
using Movie.Application.Abstractions.Authentication;
using Movie.Application.Abstractions.Email;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

/// <summary>
/// Sends a fresh confirmation code. Returns nothing on purpose: the caller must
/// not be able to tell an unknown address from a pending one.
/// </summary>
public sealed record ResendVerificationCommand(string Email) : IRequest;

public sealed class ResendVerificationCommandHandler(
    UserManager<ApplicationUser> users,
    IVerificationCodeService codes,
    IVerificationEmailSender emails) : IRequestHandler<ResendVerificationCommand>
{
    public async ValueTask<Unit> Handle(
        ResendVerificationCommand command,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());

        // Nothing is sent for an unknown address, and nothing is sent for one
        // that already finished sign-up — sending in the latter case would turn
        // this endpoint into a way to mail arbitrary people on demand.
        if (user is not null && !user.EmailConfirmed)
        {
            var code = await codes.IssueAsync(user, CodePurpose.EmailConfirmation, cancellationToken);
            await emails.SendAsync(user, CodePurpose.EmailConfirmation, code, cancellationToken);
        }

        return Unit.Value;
    }
}
