using Movie.Domain.Users;

namespace Movie.Domain.Lists;

/// <summary>
/// A list created by one person and co-edited by the members who accepted their
/// invitation. Named <c>MediaList</c> because <c>List</c> would collide with the
/// collection type.
/// </summary>
public sealed class MediaList
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Must be 1–60 characters once trimmed (constraint defined in 1e).</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Who created the list. <c>init</c> on purpose: Supabase guarded this with
    /// the <c>prevent_list_reassignment</c> trigger, because its UPDATE policy
    /// constrained which <em>rows</em> could be updated but not which
    /// <em>columns</em> could change — any member could rename the list and
    /// reassign ownership to themselves in the same statement.
    /// </summary>
    public required Guid CreatedById { get; init; }

    public ApplicationUser? CreatedBy { get; init; }

    /// <summary>
    /// The code used to join by hand. <c>set</c> because the owner can
    /// regenerate it.
    /// </summary>
    public string JoinCode { get; set; } = JoinCodeGenerator.Generate();

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Written by the <c>touch_updated_at</c> trigger in Supabase;
    /// <c>DbContext.SaveChanges</c> takes that over here (1e).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ListMember> Members { get; } = [];

    public ICollection<ListItem> Items { get; } = [];

    public ICollection<ListPoll> Polls { get; } = [];
}