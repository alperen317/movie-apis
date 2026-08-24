using Movie.Domain.Users;

namespace Movie.Application.Abstractions.Email;

/// <summary>
/// Composes and sends the code email. Handlers depend on this rather than on
/// <see cref="IEmailSender"/> plus a template, so the wording and markup stay
/// out of the application layer.
/// </summary>
public interface IVerificationEmailSender
{
    Task SendAsync(
        ApplicationUser user,
        CodePurpose purpose,
        string code,
        CancellationToken cancellationToken = default);
}