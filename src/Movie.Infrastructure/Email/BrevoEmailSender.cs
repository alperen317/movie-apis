using System.Net.Http.Json;

using Microsoft.Extensions.Options;

using Movie.Application.Abstractions.Email;

namespace Movie.Infrastructure.Email;

/// <summary>
/// The one place that actually talks to an email provider. Everything above
/// this — verification codes, list invites — goes through
/// <see cref="IEmailSender"/> and never sees Brevo's request shape.
/// </summary>
/// <remarks>
/// Failures are thrown, not swallowed: a verification code that never sends
/// leaves someone locked out, so the caller (registration, password reset)
/// needs to know it failed. The one sender that deliberately does not want
/// that — <see cref="ListInviteEmailSender"/> — catches it itself instead of
/// relying on this class to stay quiet.
/// </remarks>
public sealed class BrevoEmailSender(HttpClient client, IOptions<BrevoOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var sender = options.Value;

        using var response = await client.PostAsJsonAsync(
            "smtp/email",
            new BrevoSendRequest(
                new BrevoSender(sender.SenderEmail, sender.SenderName),
                [new BrevoRecipient(message.To)],
                message.Subject,
                message.HtmlBody),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);

            throw new InvalidOperationException(
                $"Brevo send failed with {(int)response.StatusCode}: {detail}");
        }
    }

    private sealed record BrevoSendRequest(
        BrevoSender Sender,
        BrevoRecipient[] To,
        string Subject,
        string HtmlContent);

    private sealed record BrevoSender(string Email, string Name);

    private sealed record BrevoRecipient(string Email);
}