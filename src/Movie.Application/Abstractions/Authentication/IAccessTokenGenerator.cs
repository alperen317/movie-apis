using Movie.Domain.Users;

namespace Movie.Application.Abstractions.Authentication;

/// <summary>
/// Issues the short-lived token a client sends on every request. Supabase's
/// GoTrue did this; the shape (a bearer JWT with a one-hour life, refreshed by
/// the client before it expires) is kept so the mobile client's existing
/// refresh logic still applies.
/// </summary>
public interface IAccessTokenGenerator
{
    AccessToken Generate(ApplicationUser user);
}

/// <param name="Value">The encoded JWT.</param>
/// <param name="ExpiresAtUtc">
/// Returned to the client so it can refresh ahead of expiry rather than
/// discovering the expiry through a failed request.
/// </param>
public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);