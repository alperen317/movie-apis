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
            // A conditional UPDATE rather than read-then-write: two concurrent
            // wrong guesses both reading the same Attempts value would
            // otherwise overwrite each other and only one guess would count.
            // Guarding on ConsumedAt/Attempts here too means a guess that
            // loses the race against a concurrent successful consumption (or
            // against the attempt cap being hit first) falls through to the
            // same answer a request arriving a moment later would have gotten.
            var incremented = await database.VerificationCodes
                .Where(x => x.Id == stored.Id
                    && x.ConsumedAt == null
                    && x.Attempts < VerificationCode.MaxAttempts)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.Attempts, x => x.Attempts + 1),
                    cancellationToken);

            if (incremented == 0)
            {
                return VerificationResult.Invalid;
            }

            var attempts = await database.VerificationCodes
                .Where(x => x.Id == stored.Id)
                .Select(x => x.Attempts)
                .SingleAsync(cancellationToken);

            return attempts >= VerificationCode.MaxAttempts
                ? VerificationResult.TooManyAttempts
                : VerificationResult.Invalid;
        }

        // Claims the code with the same kind of conditional UPDATE: two
        // concurrent requests holding the correct code would otherwise both
        // read ConsumedAt == null and both be told Success.
        var consumed = await database.VerificationCodes
            .Where(x => x.Id == stored.Id
                && x.ConsumedAt == null
                && x.Attempts < VerificationCode.MaxAttempts)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ConsumedAt, now),
                cancellationToken);

        return consumed == 1 ? VerificationResult.Success : VerificationResult.Invalid;
    }
}