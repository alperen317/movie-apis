using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

using Movie.Application.Abstractions;

namespace Movie.Infrastructure.Authentication;

public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? Id =>
        Guid.TryParse(
            accessor.HttpContext?.User.FindFirstValue(JwtRegisteredClaimNames.Sub),
            out var id)
            ? id
            : null;
}

/// <summary>
/// For contexts built outside a request: the migration tooling, and tests that
/// want to act as a particular user.
/// </summary>
public sealed class StaticCurrentUser(Guid? id) : ICurrentUser
{
    public static StaticCurrentUser Anonymous { get; } = new(null);

    public Guid? Id { get; } = id;
}