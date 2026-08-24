namespace Movie.Domain.Users;

/// <summary>
/// A six-digit code emailed to a user to prove they control the address.
/// </summary>
/// <remarks>
/// <para>
/// Identity ships a TOTP-based provider that would have produced these codes
/// with no table at all, but its lifetime is an undocumented implementation
/// detail: a three-minute timestep with a ±2 validation window, so a code lives
/// somewhere between six and nine minutes depending on when in the window it
/// was issued. Both numbers were measured rather than assumed, and neither is
/// part of Identity's public contract — a package upgrade could change them
/// silently.
/// </para>
/// <para>
/// Owning the lifecycle instead buys three things that provider cannot give:
/// an exact expiry, single use, and a cap on wrong guesses. The last matters
/// most — six digits is a million possibilities, which is only a real barrier
/// while the number of attempts is bounded.
/// </para>
/// </remarks>
public sealed class VerificationCode
{
    public const int Length = 6;

    /// <summary>
    /// After this many wrong guesses the code is dead and a new one must be
    /// requested, even if it has not expired.
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>Matches the Supabase default, so the flow feels unchanged.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required CodePurpose Purpose { get; init; }

    /// <summary>
    /// Hashed, never stored in the clear: a database leak should not hand over
    /// working codes for every pending sign-up and password reset.
    /// </summary>
    public required string CodeHash { get; init; }

    public required DateTime ExpiresAt { get; init; }

    public int Attempts { get; set; }

    /// <summary>Set the moment a code is accepted, which is what makes it single-use.</summary>
    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public bool IsUsableAt(DateTime utcNow) =>
        ConsumedAt is null && Attempts < MaxAttempts && utcNow <= ExpiresAt;
}