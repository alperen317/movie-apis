using Mediator;
using Microsoft.AspNetCore.Identity;
using Movie.Application.Abstractions.Authentication;
using Movie.Application.Abstractions.Email;
using Movie.Domain.Users;

namespace Movie.Application.Features.Authentication;

public sealed record RegisterCommand(string Email, string Password) : IRequest<RegisterResult>;

/// <param name="PasswordErrors">
/// Empty when the request was accepted. Password problems are the only thing
/// reported back, because they say nothing about whether the address is taken.
/// </param>
public sealed record RegisterResult(IReadOnlyList<string> PasswordErrors)
{
    public static RegisterResult Accepted { get; } = new([]);

    public bool IsAccepted => PasswordErrors.Count == 0;
}

public sealed class RegisterCommandHandler(
    UserManager<ApplicationUser> users,
    IVerificationCodeService codes,
    IVerificationEmailSender emails) : IRequestHandler<RegisterCommand, RegisterResult>
{
    public async ValueTask<RegisterResult> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var address = command.Email.Trim();

        // Validated before the account is looked up, and against a throwaway
        // user rather than a stored one. If this ran after the lookup, a
        // deliberately weak password would come back rejected for a free
        // address and accepted for a taken one — an account-existence oracle
        // dressed up as validation.
        var passwordErrors = await ValidatePasswordAsync(address, command.Password);
        if (passwordErrors.Count > 0)
        {
            return new RegisterResult(passwordErrors);
        }

        var existing = await users.FindByEmailAsync(address);

        if (existing is null)
        {
            var user = new ApplicationUser { UserName = address, Email = address };
            var created = await users.CreateAsync(user, command.Password);

            // Anything still failing here is about the address itself, and
            // saying so would leak. The caller gets the same reply either way.
            if (created.Succeeded)
            {
                await IssueAndSendAsync(user, cancellationToken);
            }
        }
        else if (!existing.EmailConfirmed)
        {
            // Registering again with an address that never finished sign-up
            // behaves as a resend, so a lost email is not a dead end.
            await IssueAndSendAsync(existing, cancellationToken);
        }

        // An address that is already confirmed gets nothing at all: no code, no
        // email, and the same response as every other case.
        return RegisterResult.Accepted;
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

    private async Task IssueAndSendAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var code = await codes.IssueAsync(user, CodePurpose.EmailConfirmation, cancellationToken);
        await emails.SendAsync(user, CodePurpose.EmailConfirmation, code, cancellationToken);
    }
}
