using Microsoft.AspNetCore.Identity;

namespace Movie.Domain.Users;

/// <summary>
/// A user of the app. Supabase split this across two tables: credentials in
/// <c>auth.users</c>, profile fields in <c>public.profiles</c>, kept in sync by
/// a trigger. That split existed only because PostgREST can't see the
/// <c>auth</c> schema; without that constraint the two collapse into one table
/// here, and the synchronization problem disappears with them.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// User-chosen display name. May be absent, in which case the client falls
    /// back to the email address (see the shared-list member roster).
    /// </summary>
    public string? DisplayName { get; set; }

    public AvatarVariant AvatarVariant { get; set; } = AvatarVariant.Beam;

    /// <summary>
    /// Hash seed for the generated avatar. When null the client falls back to
    /// <see cref="DisplayName"/>/email. It is a separate field because a fixed
    /// seed would limit the "shuffle" button to cycling through the six styles;
    /// a random seed produces genuinely different-looking avatars.
    /// </summary>
    public string? AvatarSeed { get; set; }

    /// <summary>
    /// Region code for "where to watch" (ISO 3166-1 alpha-2, e.g. <c>TR</c>).
    /// Null means the client uses the device region. It exists so a user can
    /// follow a foreign catalog independently of their device locale.
    /// </summary>
    public string? WatchRegion { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}