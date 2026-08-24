namespace Movie.Domain.Users;

/// <summary>
/// A long-lived token the client exchanges for a fresh access token.
/// </summary>
/// <remarks>
/// <para>
/// Identity's own <c>user_tokens</c> table was not reused: its key is
/// (user, provider, name), so it holds one token per user. A phone and a
/// tablet could not be signed in at the same time.
/// </para>
/// <para>
/// Tokens rotate — using one revokes it and issues a successor. That is what
/// makes theft detectable: a revoked token showing up again means two parties
/// hold the same secret, so every session for that user is dropped.
/// <see cref="ReplacedById"/> records the chain that makes this visible.
/// </para>
/// </remarks>
public sealed class RefreshToken
{
    /// <summary>256 bits of randomness, which is what makes a plain digest enough to store it.</summary>
    public const int ByteLength = 32;

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    /// <summary>
    /// SHA-256 of the token. Unlike <see cref="VerificationCode.CodeHash"/>,
    /// which protects a six-digit secret and therefore needs a deliberately
    /// slow hash, this one covers 256 bits of entropy — there is nothing to
    /// brute force, so a fast digest is the right tool.
    /// </summary>
    public required string TokenHash { get; init; }

    public required DateTime ExpiresAt { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Set on rotation, on sign-out, and on every token of a user whose token was replayed.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>The token issued in this one's place, when it was rotated.</summary>
    public Guid? ReplacedById { get; set; }

    public bool IsActiveAt(DateTime utcNow) => RevokedAt is null && utcNow <= ExpiresAt;
}