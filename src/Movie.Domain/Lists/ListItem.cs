using Movie.Domain.Media;
using Movie.Domain.Users;

namespace Movie.Domain.Lists;

/// <summary>
/// A title added to a shared list. Unique within the list: the same film can't
/// be added twice.
/// </summary>
/// <remarks>
/// Who added it is recorded, but removal rights don't follow from it: any
/// accepted member may remove any item. That is a deliberate product decision —
/// members are equals when editing content, and <see cref="AddedById"/> is only
/// for display.
/// </remarks>
public sealed class ListItem
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid ListId { get; init; }

    public MediaList? List { get; init; }

    public required int MediaId { get; init; }

    public required MediaType MediaType { get; init; }

    public required string Title { get; init; }

    public string? PosterPath { get; init; }

    public decimal? VoteAverage { get; init; }

    public string? Year { get; init; }

    public string[] Genres { get; init; } = [];

    public required Guid AddedById { get; init; }

    /// <summary>Source of the name and avatar on the "who added this" badge.</summary>
    public ApplicationUser? AddedBy { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
