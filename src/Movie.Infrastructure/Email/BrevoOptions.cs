namespace Movie.Infrastructure.Email;

public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    /// <summary>
    /// Never committed. Supplied by the environment outside development, the
    /// same way <c>Jwt:SigningKey</c> is.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    public string SenderEmail { get; init; } = string.Empty;

    public string SenderName { get; init; } = string.Empty;
}
