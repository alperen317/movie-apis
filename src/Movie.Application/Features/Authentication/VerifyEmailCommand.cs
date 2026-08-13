using Mediator;
using Microsoft.AspNetCore.Identity;
using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

public sealed record VerifyEmailCommand(string Email, string Code) : IRequest<VerificationResult>;

public sealed class VerifyEmailCommandHandler(
    UserManager<ApplicationUser> users,
    IVerificationCodeService codes) : IRequestHandler<VerifyEmailCommand, VerificationResult>
{
    public async ValueTask<VerificationResult> Handle(
        VerifyEmailCommand command,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());

        // An unknown address is answered exactly as a wrong code would be.
        if (user is null)
        {
            return VerificationResult.Invalid;
        }

        if (user.EmailConfirmed)
        {
            return VerificationResult.Invalid;
        }

        var result = await codes.ConsumeAsync(
            user,
            CodePurpose.EmailConfirmation,
            command.Code.Trim(),
            cancellationToken);

        if (result != VerificationResult.Success)
        {
            return result;
        }

        // Redeeming the code and acting on it happen in the same request. Split
        // across two calls, the code would stay live in between and could be
        // replayed by anyone who saw it.
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);

        return VerificationResult.Success;
    }
}
