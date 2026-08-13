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

    /// <summary>Matches the Supabase default, so client refresh timing is unchanged.</summary>
    public int AccessTokenMinutes { get; init; } = 60;
}
