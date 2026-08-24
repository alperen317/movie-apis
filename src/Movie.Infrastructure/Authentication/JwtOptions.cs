namespace Movie.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC-SHA256 needs at least 256 bits of key material; anything shorter is
    /// rejected at startup rather than silently weakening every token.
    /// </summary>
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Never committed. Supplied by user-secrets in development and by the
    /// environment in every other case.
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>
    /// Deliberately shorter than the hour Supabase used. An access token cannot
    /// be revoked, so signing out stops renewal but leaves the token usable —
    /// for writes as well as reads — until it expires. This is how long that
    /// window stays open.
    /// </summary>
    public int AccessTokenMinutes { get; init; } = 15;
}