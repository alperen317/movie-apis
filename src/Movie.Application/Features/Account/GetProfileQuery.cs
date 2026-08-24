using Mediator;
using Microsoft.AspNetCore.Identity;
using Movie.Domain.Users;

namespace Movie.Application.Features.Account;

public sealed record GetProfileQuery(Guid UserId) : IRequest<ProfileDto?>;

public sealed record ProfileDto(
    Guid Id,
    string Email,
    string? DisplayName,
    AvatarVariant AvatarVariant,
    string? AvatarSeed,
    string? WatchRegion,
    DateTime CreatedAt)
{
    public static ProfileDto From(ApplicationUser user) => new(
        user.Id,
        user.Email ?? string.Empty,
        user.DisplayName,
        user.AvatarVariant,
        user.AvatarSeed,
        user.WatchRegion,
        user.CreatedAt);
}

public sealed class GetProfileQueryHandler(UserManager<ApplicationUser> users)
    : IRequestHandler<GetProfileQuery, ProfileDto?>
{
    public async ValueTask<ProfileDto?> Handle(GetProfileQuery query, CancellationToken cancellationToken)
    {
        // Read from storage rather than from the token's claims. A token stays
        // valid for its full hour, so it can outlive the account it names —
        // going to the row is what makes a deleted user's token stop working.
        var user = await users.FindByIdAsync(query.UserId.ToString());

        return user is null ? null : ProfileDto.From(user);
    }
}
