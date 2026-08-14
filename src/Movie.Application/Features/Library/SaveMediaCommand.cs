using Mediator;
using Movie.Application.Abstractions;
using Movie.Application.Abstractions.Library;
using Movie.Domain.Library;

namespace Movie.Application.Features.Library;

/// <summary>
/// Saves one or many titles to favorites or the watchlist.
/// </summary>
/// <remarks>
/// One command serves both endpoints. Saving a single title and importing a
/// library differ only in how many titles arrive, and the "skip what is already
/// there" rule that made the importer re-runnable is exactly the rule that
/// makes a double tap harmless.
/// </remarks>
public sealed record SaveMediaCommand(IReadOnlyList<TitleSnapshot> Titles, ListType ListType)
    : IRequest<int>;

public sealed class SaveMediaCommandHandler(ISavedMediaStore saved)
    : IRequestHandler<SaveMediaCommand, int>
{
    /// <returns>How many titles were not saved already.</returns>
    public ValueTask<int> Handle(SaveMediaCommand command, CancellationToken cancellationToken) =>
        new(saved.SaveAsync(command.Titles, command.ListType, cancellationToken));
}
