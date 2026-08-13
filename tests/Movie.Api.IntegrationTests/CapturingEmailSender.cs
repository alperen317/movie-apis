using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Movie.Application.Abstractions.Email;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Stands in for the real sender and keeps what would have been delivered.
/// It substitutes at the transport level rather than above the templates, so a
/// test that reads a code out of a message is also checking that the template
/// actually carries one.
/// </summary>
public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyCollection<EmailMessage> Sent => _sent;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public EmailMessage? LastTo(string recipient) =>
        _sent.LastOrDefault(m => string.Equals(m.To, recipient, StringComparison.OrdinalIgnoreCase));

    /// <summary>The six-digit code from the most recent message to an address.</summary>
    public string CodeSentTo(string recipient)
    {
        var message = LastTo(recipient)
            ?? throw new InvalidOperationException($"No email was sent to {recipient}.");

        var match = CodeElement().Match(message.HtmlBody);

        return match.Success
            ? match.Groups["code"].Value
            : throw new InvalidOperationException($"No code found in the email to {recipient}.");
    }

    public void Clear() => _sent.Clear();

    /// <summary>
    /// Anchored to the element that holds the code rather than to "six digits
    /// anywhere". The loose version matched the #131313 background colour in
    /// the template's own styling, so every test read that instead of the code.
    /// </summary>
    [GeneratedRegex("""id="verification-code"[^>]*>(?<code>\d{6})<""")]
    private static partial Regex CodeElement();
}
