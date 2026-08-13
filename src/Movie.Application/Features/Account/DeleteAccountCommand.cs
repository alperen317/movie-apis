using Mediator;
using Microsoft.AspNetCore.Identity;
using Movie.Domain.Users;

namespace Movie.Application.Features.Account;

/// <summary>
/// Deletes the account and, through the cascades on every table that points at
/// it, everything the user ever saved.
/// </summary>
/// <remarks>
/// Required for App Store guideline 5.1.1(v). No password is asked for: the
/// client already makes the user type a confirmation word, and this matches
/// what the Supabase RPC did.
/// </remarks>
public sealed record DeleteAccountCommand(Guid UserId) : IRequest<bool>;

public sealed class DeleteAccountCommandHandler(UserManager<ApplicationUser> users)
    : IRequestHandler<DeleteAccountCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId.ToString());

        if (user is null)
        {
            return false;
        }

        // Refresh tokens, verification codes, saved media, watch log, list
        // membership — all of it goes with the row. A shared list this user
        // created goes too, for its other members as well; that was the
        // Supabase schema's designed behaviour and is kept deliberately.
        var result = await users.DeleteAsync(user);

        return result.Succeeded;
    }
}
