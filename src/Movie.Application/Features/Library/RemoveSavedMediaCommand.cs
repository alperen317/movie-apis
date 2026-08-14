using Mediator;
using Movie.Application.Abstractions.Library;
using Movie.Domain.Library;
using Movie.Domain.Media;

namespace Movie.Application.Features.Library;

/// <summary>
/// Unfavorites a title, or takes it off the watchlist. The two are separate
/// rows, so removing from one leaves the other alone.
/// </summary>
public sealed record RemoveSavedMediaCommand(int MediaId, MediaType MediaType, ListType ListType)
    : IRequest<bool>;

public sealed class RemoveSavedMediaCommandHandler(ISavedMediaStore saved)
    : IRequestHandler<RemoveSavedMediaCommand, bool>
{
    public ValueTask<bool> Handle(
        RemoveSavedMediaCommand command,
        CancellationToken cancellationToken) =>
        new(saved.RemoveAsync(
            command.MediaId,
            command.MediaType,
            command.ListType,
            cancellationToken));
}
