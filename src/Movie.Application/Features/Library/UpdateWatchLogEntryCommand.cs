using Mediator;

using Movie.Application.Abstractions.Library;

namespace Movie.Application.Features.Library;

/// <summary>
/// Corrects a recorded watch: when it happened, what it was scored, what was
/// said about it.
/// </summary>
/// <remarks>
/// The title is not among them. What is being corrected is the record of
/// watching something, not which thing was watched — for that, the entry is
/// deleted and a new one logged.
/// </remarks>
public sealed record UpdateWatchLogEntryCommand(
    Guid Id,
    DateTime WatchedAt,
    int? Rating,
    string? Note) : IRequest<WatchLogDto?>;

public sealed class UpdateWatchLogEntryCommandHandler(IWatchLogStore watchLog)
    : IRequestHandler<UpdateWatchLogEntryCommand, WatchLogDto?>
{
    /// <returns>Null when the caller has no such entry.</returns>
    public async ValueTask<WatchLogDto?> Handle(
        UpdateWatchLogEntryCommand command,
        CancellationToken cancellationToken)
    {
        var updated = await watchLog.UpdateAsync(
            command.Id,
            command.WatchedAt,
            command.Rating,
            command.Note,
            cancellationToken);

        return updated is null ? null : WatchLogDto.From(updated);
    }
}