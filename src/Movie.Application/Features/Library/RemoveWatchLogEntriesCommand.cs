using Mediator;

using Movie.Application.Abstractions.Library;

namespace Movie.Application.Features.Library;

/// <summary>
/// Deletes recorded watches by id.
/// </summary>
/// <remarks>
/// Takes a list because unmarking a title as watched removes every entry for
/// it, not the latest one. The mark the app shows means "is there any entry at
/// all", so an earlier rewatch left behind would keep the title looking
/// watched. The client is what decides which ids that comes to.
/// </remarks>
public sealed record RemoveWatchLogEntriesCommand(IReadOnlyList<Guid> Ids) : IRequest<int>;

public sealed class RemoveWatchLogEntriesCommandHandler(IWatchLogStore watchLog)
    : IRequestHandler<RemoveWatchLogEntriesCommand, int>
{
    /// <returns>
    /// How many entries went. Fewer than were asked for means some were not the
    /// caller's, which is not an error — they were simply never visible.
    /// </returns>
    public ValueTask<int> Handle(
        RemoveWatchLogEntriesCommand command,
        CancellationToken cancellationToken) =>
        new(watchLog.RemoveAsync(command.Ids, cancellationToken));
}