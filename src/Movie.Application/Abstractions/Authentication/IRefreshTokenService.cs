using Movie.Domain.Users;

namespace Movie.Application.Abstractions.Authentication;

public interface IRefreshTokenService
{
    Task<string> IssueAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Spends a refresh token and issues its successor. Presenting one that was
    /// already spent drops every session the user has, on the assumption that
    /// two parties now hold the same secret.
    /// </summary>
    Task<RefreshOutcome> RotateAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Sign-out. Unknown or already-revoked tokens are accepted quietly.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends every session a user has. Used when the password changes: whoever
    /// prompted the reset should not be left holding a working session.
    /// </summary>
    Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <param name="UserId">Null when the token was unusable, for any reason.</param>
/// <param name="RefreshToken">The replacement, when rotation succeeded.</param>
public sealed record RefreshOutcome(Guid? UserId, string? RefreshToken)
{
    public static RefreshOutcome Rejected { get; } = new(null, null);

    public bool Succeeded => UserId is not null && RefreshToken is not null;
}

/// <summary>What a client receives once it is signed in.</summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken);