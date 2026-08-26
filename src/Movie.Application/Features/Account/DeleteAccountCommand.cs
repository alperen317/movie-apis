using Mediator;

using Microsoft.AspNetCore.Identity;

using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;
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

public sealed class DeleteAccountCommandHandler(
    UserManager<ApplicationUser> users,
    IListStore lists,
    IListEventPublisher events)
    : IRequestHandler<DeleteAccountCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId.ToString());

        if (user is null)
        {
            return false;
        }

        // lists.MineAsync reads ICurrentUser rather than command.UserId --
        // fine only because the one caller of this command (DELETE /me)
        // always deletes its own account, so the two never differ.
        var memberships = await lists.MineAsync(cancellationToken);
        var owned = new List<(MediaList List, IReadOnlyList<ListMember> Members)>();

        foreach (var list in memberships.Where(list => list.CreatedById == command.UserId))
        {
            owned.Add((list, await lists.MembersAsync(list, cancellationToken)));
        }

        var joined = memberships.Where(list => list.CreatedById != command.UserId).ToList();

        // Refresh tokens, verification codes, saved media, watch log, list
        // membership — all of it goes with the row. A shared list this user
        // created goes too, for its other members as well; that was the
        // Supabase schema's designed behaviour and is kept deliberately.
        var result = await users.DeleteAsync(user);

        if (!result.Succeeded)
        {
            return false;
        }

        // Broadcast only once the delete has actually committed. Unlike
        // DeleteListCommandHandler's single DeleteAsync -- which either
        // succeeds or throws, so nothing after a pre-emptive broadcast would
        // run anyway -- UserManager.DeleteAsync can fail and hand back a
        // reason. Broadcasting first here would mean a co-member could be
        // told a list is gone, or refetch a roster that hasn't actually
        // changed yet, for a delete that never happened.
        foreach (var (list, _) in owned)
        {
            await events.ListDeletedAsync(list.Id, cancellationToken);
        }

        foreach (var list in joined)
        {
            await events.MembersChangedAsync(list.Id, cancellationToken);
        }

        // Evict only after the delete actually commits -- see
        // DeleteListCommandHandler for why doing it the other way around
        // risks stranding a connection out of a group for rows that, having
        // never reached this point, still exist.
        foreach (var (list, members) in owned)
        {
            foreach (var member in members)
            {
                await events.MemberEvictedAsync(list.Id, member.UserId, cancellationToken);
            }
        }

        foreach (var list in joined)
        {
            await events.MemberEvictedAsync(list.Id, command.UserId, cancellationToken);
        }

        return true;
    }
}