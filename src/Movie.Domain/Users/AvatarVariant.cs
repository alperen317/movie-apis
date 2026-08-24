namespace Movie.Domain.Users;

/// <summary>
/// Style of the generated (boring-avatars) profile picture. These values must
/// stay in sync with <c>lib/avatar/generate.ts</c> in the mobile client, which
/// draws the avatar from this name — a mismatch means it can't render.
/// </summary>
public enum AvatarVariant
{
    Marble,
    Beam,
    Bauhaus,
    Ring,
    Pixel,
    Sunset,
}