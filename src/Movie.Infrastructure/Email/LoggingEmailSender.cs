using Microsoft.Extensions.Logging;
using Movie.Application.Abstractions.Email;

namespace Movie.Infrastructure.Email;

/// <summary>
/// Writes the email to the log instead of sending it, so the sign-up and reset
/// flows are usable locally without an email provider. The real Brevo sender
/// arrives in phase 6.
/// </summary>
/// <remarks>
/// This logs the message body, which contains a live verification code. That is
/// the point in development and unacceptable anywhere else, so
/// <c>AddInfrastructure</c> only registers it outside production.
/// </remarks>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Email not sent (development sender). To: {Recipient}. Subject: {Subject}.\n{Body}",
            message.To,
            message.Subject,
            message.HtmlBody);

        return Task.CompletedTask;
    }
}
