using Mediator;

using Microsoft.AspNetCore.Identity;

using Movie.Domain.Users;

namespace Movie.Application.Features.Account;

/// <summary>
/// Replaces the editable part of a profile wholesale. Every field is written as
/// given, so omitting one clears it.
/// </summary>
/// <remarks>
/// A partial update would need to tell "this field was left out" apart from
/// "set this field to null", which JSON does not express without a wrapper type
/// around every property — and the client always holds the whole profile
/// anyway, since it loaded it to render the settings screen. Sending the state
/// it wants is simpler to reason about and idempotent.
/// </remarks>
public sealed record UpdateProfileCommand(
    Guid UserId,
    string? DisplayName,
    AvatarVariant AvatarVariant,
    string? AvatarSeed,
    string? WatchRegion) : IRequest<ProfileDto?>;

public sealed class UpdateProfileCommandHandler(UserManager<ApplicationUser> users)
    : IRequestHandler<UpdateProfileCommand, ProfileDto?>
{
    public async ValueTask<ProfileDto?> Handle(
        UpdateProfileCommand command,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId.ToString());

        if (user is null)
        {
            return null;
        }

        user.DisplayName = Trimmed(command.DisplayName);
        user.AvatarVariant = command.AvatarVariant;
        user.AvatarSeed = Trimmed(command.AvatarSeed);
        user.WatchRegion = Trimmed(command.WatchRegion)?.ToUpperInvariant();

        await users.UpdateAsync(user);

        return ProfileDto.From(user);
    }

    /// <summary>
    /// Whitespace-only input means the user cleared the field, so it is stored
    /// as absent rather than as a string of spaces.
    /// </summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}