using Movie.Application.Abstractions.Email;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Email;

public sealed class VerificationEmailSender(IEmailSender sender) : IVerificationEmailSender
{
    public Task SendAsync(
        ApplicationUser user,
        CodePurpose purpose,
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            throw new InvalidOperationException("Cannot send a verification code to a user with no email.");
        }

        return sender.SendAsync(VerificationEmailTemplates.For(user.Email, purpose, code), cancellationToken);
    }
}