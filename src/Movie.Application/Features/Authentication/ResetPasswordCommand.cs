using Mediator;

using Microsoft.AspNetCore.Identity;

using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

public sealed record ResetPasswordCommand(string Email, string Code, string NewPassword)
    : IRequest<ResetPasswordResult>;

/// <param name="PasswordErrors">
/// Non-empty when the new password fails policy. Says nothing about whether
/// the account exists or the code is valid -- see the handler for why.
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
        var email = command.Email.Trim();

        // Validated before the account is looked up, and against a throwaway
        // user rather than a stored one -- see RegisterCommandHandler for the
        // same pattern. Checking a real user first would mean a deliberately
        // weak password comes back as "weak" for a registered, confirmed
        // address and as "invalid code" for everything else: an
        // account-existence oracle dressed up as validation, and one that
        // doesn't even require guessing the code.
        var passwordErrors = await ValidatePasswordAsync(email, command.NewPassword);
        if (passwordErrors.Count > 0)
        {
            return ResetPasswordResult.WeakPassword(passwordErrors);
        }

        var user = await users.FindByEmailAsync(email);

        if (user is null || !user.EmailConfirmed)
        {
            return ResetPasswordResult.Failed(VerificationResult.Invalid);
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

    private async Task<IReadOnlyList<string>> ValidatePasswordAsync(string email, string password)
    {
        var candidate = new ApplicationUser { UserName = email, Email = email };
        var errors = new List<string>();

        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, candidate, password);
            if (!result.Succeeded)
            {
                errors.AddRange(result.Errors.Select(error => error.Description));
            }
        }

        return errors;
    }
}