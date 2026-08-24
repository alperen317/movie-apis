using Mediator;

using Movie.Application.Abstractions.Library;

namespace Movie.Application.Features.Library;

/// <summary>
/// Records one or many watches.
/// </summary>
/// <remarks>
/// One command for both endpoints, as with saving titles: logging a single
/// watch and importing a history differ only in how many arrive, and neither
/// has anything to reconcile against what is already there.
/// </remarks>
public sealed record LogWatchCommand(IReadOnlyList<LoggedWatch> Watches)
    : IRequest<IReadOnlyList<WatchLogDto>>;

public sealed class LogWatchCommandHandler(IWatchLogStore watchLog)
    : IRequestHandler<LogWatchCommand, IReadOnlyList<WatchLogDto>>
{
    public async ValueTask<IReadOnlyList<WatchLogDto>> Handle(
        LogWatchCommand command,
        CancellationToken cancellationToken)
    {
        var written = await watchLog.AddAsync(command.Watches, cancellationToken);

        return [.. written.Select(WatchLogDto.From)];
    }
}