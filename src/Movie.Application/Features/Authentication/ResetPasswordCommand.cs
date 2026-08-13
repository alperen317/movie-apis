using Mediator;
using Microsoft.AspNetCore.Identity;
using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword)
    : IRequest<ResetPasswordResult>;

/// <param name="PasswordErrors">
/// Non-empty when the new password fails policy. Safe to report: reaching this
/// point requires a live code, which only arrives in the account's own inbox.
/// </param>
public sealed record ResetPasswordResult(
    VerificationResult Outcome,
    IReadOnlyList<string> PasswordErrors)
{
    public static ResetPasswordResult Succeeded { get; } = new(VerificationResult.Success, []);

    public static ResetPasswordResult Failed(VerificationResult outcome) => new(outcome, []);

    public static ResetPasswordResult WeakPassword(IReadOnlyList<string> errors) =>
        new(VerificationResult.Success, errors);
}

public sealed class ResetPasswordCommandHandler(
    UserManager<ApplicationUser> users,
    IVerificationCodeService codes,
    IRefreshTokenService refreshTokens) : IRequestHandler<ResetPasswordCommand, ResetPasswordResult>
{
    public async ValueTask<ResetPasswordResult> Handle(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());

        if (user is null || !user.EmailConfirmed)
        {
            return ResetPasswordResult.Failed(VerificationResult.Invalid);
        }

        // Checked before the code is spent. The other way round, typing a
        // too-short password would burn the code and send the user back to
        // their inbox for a new one.
        var passwordErrors = await ValidatePasswordAsync(user, command.NewPassword);
        if (passwordErrors.Count > 0)
        {
            return ResetPasswordResult.WeakPassword(passwordErrors);
        }

        var outcome = await codes.ConsumeAsync(
            user,
            CodePurpose.PasswordReset,
            command.Code.Trim(),
            cancellationToken);

        if (outcome != VerificationResult.Success)
        {
            return ResetPasswordResult.Failed(outcome);
        }

        // Identity's own token is only the mechanism for the change; the code
        // above is what authorised it. Resetting this way also rolls the
        // security stamp, which is what Identity keys its own invalidation on.
        var resetToken = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, resetToken, command.NewPassword);

        if (!result.Succeeded)
        {
            return ResetPasswordResult.WeakPassword(
                [.. result.Errors.Select(error => error.Description)]);
        }

        // The reason someone resets a password is usually that somebody else
        // knows it. Leaving that person's sessions alive would defeat the whole
        // exercise.
        await refreshTokens.RevokeAllAsync(user.Id, cancellationToken);

        return ResetPasswordResult.Succeeded;
    }

    private async Task<IReadOnlyList<string>> ValidatePasswordAsync(
        ApplicationUser user,
        string password)
    {
        var errors = new List<string>();

        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors.Select(error => error.Description));
            }
        }

        return errors;
    }
}
