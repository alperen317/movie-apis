using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Authentication;

public sealed class VerificationCodeService(
    MovieDbContext database,
    IPasswordHasher<ApplicationUser> hasher) : IVerificationCodeService
{
    private const string Digits = "0123456789";

    public async Task<string> IssueAsync(
        ApplicationUser user,
        CodePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        // Clearing the user's earlier codes is what makes "resend" mean
        // "replace" rather than "add a second working code", and it keeps the
        // table from accumulating dead rows without needing a sweeper job.
        await database.VerificationCodes
            .Where(x => x.UserId == user.Id && x.Purpose == purpose)
            .ExecuteDeleteAsync(cancellationToken);

        var code = RandomNumberGenerator.GetString(Digits, VerificationCode.Length);

        database.VerificationCodes.Add(new VerificationCode
        {
            UserId = user.Id,
            Purpose = purpose,

            // The hasher is Identity's PBKDF2 one. It is slower than a plain
            // digest by design, which is the point: six digits is a million
            // possibilities, and a fast hash would let anyone who reads the
            // table recover every live code offline in moments.
            CodeHash = hasher.HashPassword(user, code),
            ExpiresAt = DateTime.UtcNow.Add(VerificationCode.Lifetime),
        });

        await database.SaveChangesAsync(cancellationToken);

        return code;
    }

    public async Task<VerificationResult> ConsumeAsync(
        ApplicationUser user,
        CodePurpose purpose,
        string code,
        CancellationToken cancellationToken = default)
    {
        var stored = await database.VerificationCodes
            .Where(x => x.UserId == user.Id && x.Purpose == purpose && x.ConsumedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return VerificationResult.Invalid;
        }

        var now = DateTime.UtcNow;

        if (stored.Attempts >= VerificationCode.MaxAttempts)
        {
            return VerificationResult.TooManyAttempts;
        }

        if (now > stored.ExpiresAt)
        {
            return VerificationResult.Expired;
        }

        if (hasher.VerifyHashedPassword(user, stored.CodeHash, code) == PasswordVerificationResult.Failed)
        {
            // Counted before returning, so guessing costs an attempt whether or
            // not the caller comes back to look at the result.
            stored.Attempts++;
            await database.SaveChangesAsync(cancellationToken);

            return stored.Attempts >= VerificationCode.MaxAttempts
                ? VerificationResult.TooManyAttempts
                : VerificationResult.Invalid;
        }

        stored.ConsumedAt = now;
        await database.SaveChangesAsync(cancellationToken);

        return VerificationResult.Success;
    }
}
