using System.Security.Cryptography;
using System.Text;

using Microsoft.EntityFrameworkCore;

using Movie.Application.Abstractions.Authentication;
using Movie.Domain.Users;
using Movie.Infrastructure.Persistence;

namespace Movie.Infrastructure.Authentication;

public sealed class RefreshTokenService(MovieDbContext database) : IRefreshTokenService
{
    public async Task<string> IssueAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        var token = CreateToken();

        database.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(token),
            ExpiresAt = DateTime.UtcNow.Add(RefreshToken.Lifetime),
        });

        await database.SaveChangesAsync(cancellationToken);

        return token;
    }

    public async Task<RefreshOutcome> RotateAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(refreshToken, cancellationToken);

        if (stored is null)
        {
            return RefreshOutcome.Rejected;
        }

        var now = DateTime.UtcNow;

        if (stored.RevokedAt is not null)
        {
            // Being revoked is not by itself evidence of theft. A token retired
            // by sign-out is simply stale, and a client retrying with it should
            // not cost the user their other devices.
            //
            // A rotated one is different: it was exchanged for a successor, so
            // the legitimate client has already moved on and would never send
            // it again. Seeing it means two parties hold the same secret, and
            // since neither can be identified as the thief, every session goes.
            if (stored.ReplacedById is not null)
            {
                await RevokeEveryTokenForAsync(stored.UserId, now, cancellationToken);
            }

            return RefreshOutcome.Rejected;
        }

        if (now > stored.ExpiresAt)
        {
            return RefreshOutcome.Rejected;
        }

        var replacement = CreateToken();
        var issued = new RefreshToken
        {
            UserId = stored.UserId,
            TokenHash = Hash(replacement),
            ExpiresAt = now.Add(RefreshToken.Lifetime),
        };

        database.RefreshTokens.Add(issued);

        stored.RevokedAt = now;
        stored.ReplacedById = issued.Id;

        await database.SaveChangesAsync(cancellationToken);

        return new RefreshOutcome(stored.UserId, replacement);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var stored = await FindAsync(refreshToken, cancellationToken);

        // Signing out with a token that is unknown or already dead is not an
        // error: the caller wanted to end up signed out, and they are.
        if (stored is null || stored.RevokedAt is not null)
        {
            return;
        }

        stored.RevokedAt = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    private Task<RefreshToken?> FindAsync(string refreshToken, CancellationToken cancellationToken)
    {
        // Looked up by hash rather than compared row by row, so the database
        // index does the work and no comparison timing is exposed.
        var hash = Hash(refreshToken);

        return database.RefreshTokens.FirstOrDefaultAsync(
            token => token.TokenHash == hash,
            cancellationToken);
    }

    public Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default) =>
        RevokeEveryTokenForAsync(userId, DateTime.UtcNow, cancellationToken);

    private async Task RevokeEveryTokenForAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken) =>
        await database.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                cancellationToken);

    private static string CreateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(RefreshToken.ByteLength));

    private static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}