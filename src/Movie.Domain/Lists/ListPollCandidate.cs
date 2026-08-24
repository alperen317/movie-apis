namespace Movie.Domain.Lists;

/// <summary>
/// A list item nominated in a poll. The same item can't be nominated twice in
/// one poll.
/// </summary>
/// <remarks>
/// It links to the <see cref="ListItem"/> rather than copying its content, so
/// removing the item from the list removes its candidacy too — a poll never
/// keeps showing a title that is no longer there.
/// </remarks>
public sealed class ListPollCandidate
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required Guid PollId { get; init; }

    public ListPoll? Poll { get; init; }

    public required Guid ListItemId { get; init; }

    public ListItem? ListItem { get; init; }

    public ICollection<ListPollVote> Votes { get; } = [];
}