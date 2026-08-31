using Microsoft.AspNetCore.Identity;

using Movie.Domain.Users;

namespace Movie.Infrastructure.Authentication;

/// <summary>
/// Verifies both the hashes Identity writes going forward and the bcrypt
/// hashes carried over from the old Supabase/GoTrue accounts (see
/// <c>tools/SupabaseImport</c>), so a migrated user can sign in with the
/// password they already know.
/// </summary>
/// <remarks>
/// A bcrypt match returns <see cref="PasswordVerificationResult.SuccessRehashNeeded"/>,
/// which is not specific to bcrypt — it is Identity's own signal that the
/// stored hash should be replaced. <c>UserManager.CheckPasswordAsync</c> acts
/// on it by calling <see cref="HashPassword"/> and saving the result, so a
/// migrated account is silently rewritten to a PBKDF2 hash the first time it
/// signs in successfully, with nothing for the user to notice or do. New
/// accounts never see a bcrypt hash at all, so this only ever fires once per
/// migrated user.
/// </remarks>
public sealed class LegacyPasswordHasher : IPasswordHasher<ApplicationUser>
{
    private readonly PasswordHasher<ApplicationUser> inner = new();

    public string HashPassword(ApplicationUser user, string password) =>
        inner.HashPassword(user, password);

    public PasswordVerificationResult VerifyHashedPassword(
        ApplicationUser user,
        string hashedPassword,
        string providedPassword)
    {
        if (!IsBCryptHash(hashedPassword))
        {
            return inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }

        return BCrypt.Net.BCrypt.Verify(providedPassword, hashedPassword)
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Failed;
    }

    /// <summary>
    /// bcrypt's own hashes carry their algorithm as a prefix
    /// (<c>$2a$</c>/<c>$2b$</c>/<c>$2y$</c>, the three variants GoTrue could
    /// have produced); Identity's PBKDF2 hashes never start with <c>$</c>, so
    /// this is enough to tell the two apart without guessing.
    /// </summary>
    private static bool IsBCryptHash(string hash) =>
        hash.StartsWith("$2a$", StringComparison.Ordinal)
        || hash.StartsWith("$2b$", StringComparison.Ordinal)
        || hash.StartsWith("$2y$", StringComparison.Ordinal);
}