using Mediator;
using Movie.Application.Abstractions.Authentication;

namespace Movie.Application.Features.Authentication;

/// <summary>
/// Ends one session. Other devices keep theirs, since each holds its own token.
/// </summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest;

public sealed class LogoutCommandHandler(IRefreshTokenService refreshTokens)
    : IRequestHandler<LogoutCommand>
{
    public async ValueTask<Unit> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        await refreshTokens.RevokeAsync(command.RefreshToken, cancellationToken);

        return Unit.Value;
    }
}
