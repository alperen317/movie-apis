using Movie.Domain.Media;

namespace Movie.Domain.Library;

/// <summary>
/// A title saved to favorites or the watchlist. Unique per user, list type and
/// title: the same film can't be favorited twice, but it can sit in favorites
/// and the watchlist at once.
/// </summary>
/// <remarks>
/// Every field is <c>init</c>: this table had no UPDATE policy in Supabase at
/// all (only select/insert/delete), so a row never changed after being written.
/// Users remove and re-add instead.
/// </remarks>
public sealed class SavedMedia
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid UserId { get; init; }

    public required ListType ListType { get; init; }

    public required MediaSnapshot Media { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
