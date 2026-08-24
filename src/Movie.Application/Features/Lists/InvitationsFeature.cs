using Mediator;
using Movie.Application.Abstractions.Email;
using Movie.Application.Abstractions.Lists;
using Movie.Domain.Lists;

namespace Movie.Application.Features.Lists;

/// <summary>
/// Invites an address to a list.
/// </summary>
/// <remarks>
/// Open to every accepted member, not the creator alone — bringing people in is
/// part of a list being shared, and it is what the Supabase function allowed.
/// </remarks>
public sealed record InviteToListCommand(Guid ListId, string Email) : IRequest<InviteResponse>;

/// <param name="Outcome">
/// Null <paramref name="Membership"/> is not an error to report in detail. See
/// <see cref="IInvitationStore.InviteAsync"/> for why the failures collapse
/// into one.
/// </param>
public sealed record InviteResponse(InviteOutcome? Outcome, ListMemberDto? Membership)
{
    /// <summary>The caller is not a member of the list, or there is no list.</summary>
    public static InviteResponse Unreachable => new(Outcome: null, Membership: null);
}

public sealed class InviteToListCommandHandler(
    IListAccess access,
    IInvitationStore invitations,
    IListEventPublisher events,
    IListInviteEmailSender emails)
    : IRequestHandler<InviteToListCommand, InviteResponse>
{
    public async ValueTask<InviteResponse> Handle(
        InviteToListCommand command,
        CancellationToken cancellationToken)
    {
        // Checked before the address is looked at, exactly as the Supabase
        // function did. A list the caller has nothing to do with gets the same
        // answer whether or not it exists, so no address is involved in it.
        var list = await access.ForMemberAsync(command.ListId, cancellationToken);

        if (list is null)
        {
            return InviteResponse.Unreachable;
        }

        var result = await invitations.InviteAsync(list, command.Email, cancellationToken);

        if (result.Membership is not null)
        {
            await events.MembersChangedAsync(list.Id, cancellationToken);
            await emails.SendAsync(result.Membership, list.Name, cancellationToken);
        }

        return new InviteResponse(
            result.Outcome,
            result.Membership is null ? null : ListMemberDto.From(result.Membership));
    }
}

/// <summary>
/// The invitations waiting for the caller to answer.
/// </summary>
public sealed record GetPendingInvitesQuery : IRequest<IReadOnlyList<PendingInviteDto>>;

/// <param name="InvitedByEmail">
/// Who sent it, so the card can say so. Visible to somebody who is not a member
/// yet on purpose: an invitation nobody appears to have sent is one nobody can
/// judge.
/// </param>
public sealed record PendingInviteDto(
    Guid MembershipId,
    Guid ListId,
    string ListName,
    string? InvitedByEmail,
    DateTime CreatedAt)
{
    public static PendingInviteDto From(ListMember invitation) => new(
        invitation.Id,
        invitation.ListId,
        invitation.List?.Name ?? string.Empty,
        invitation.InvitedBy?.Email,
        invitation.CreatedAt);
}

public sealed class GetPendingInvitesQueryHandler(IInvitationStore invitations)
    : IRequestHandler<GetPendingInvitesQuery, IReadOnlyList<PendingInviteDto>>
{
    public async ValueTask<IReadOnlyList<PendingInviteDto>> Handle(
        GetPendingInvitesQuery query,
        CancellationToken cancellationToken)
    {
        var pending = await invitations.PendingForMeAsync(cancellationToken);

        return [.. pending.Select(PendingInviteDto.From)];
    }
}

/// <summary>
/// Accepts or declines an invitation.
/// </summary>
public sealed record RespondToInviteCommand(Guid MembershipId, bool Accept) : IRequest<bool>;

public sealed class RespondToInviteCommandHandler(
    IListAccess access,
    IInvitationStore invitations,
    IListEventPublisher events)
    : IRequestHandler<RespondToInviteCommand, bool>
{
    /// <returns>False when there is no such invitation of the caller's to answer.</returns>
    public async ValueTask<bool> Handle(
        RespondToInviteCommand command,
        CancellationToken cancellationToken)
    {
        var invitation = await access.MyInvitationAsync(command.MembershipId, cancellationToken);

        if (invitation is null)
        {
            return false;
        }

        await invitations.RespondAsync(invitation, command.Accept, cancellationToken);

        await events.MembersChangedAsync(invitation.ListId, cancellationToken);

        return true;
    }
}

/// <summary>
/// Joins a list by typing its code.
/// </summary>
/// <remarks>
/// Membership is immediate, with no invitation and nobody's approval. Holding
/// the code <em>is</em> the authorization, which is what makes the code worth
/// generating carefully and worth counting attempts at.
/// </remarks>
public sealed record JoinListByCodeCommand(string Code) : IRequest<SharedListDto?>;

public sealed class JoinListByCodeCommandHandler(
    IInvitationStore invitations,
    IListEventPublisher events)
    : IRequestHandler<JoinListByCodeCommand, SharedListDto?>
{
    /// <returns>Null when no list has that code.</returns>
    public async ValueTask<SharedListDto?> Handle(
        JoinListByCodeCommand command,
        CancellationToken cancellationToken)
    {
        var list = await invitations.JoinByCodeAsync(command.Code, cancellationToken);

        if (list is null)
        {
            return null;
        }

        await events.MembersChangedAsync(list.Id, cancellationToken);

        // A member by the time this returns, so the code comes back with it —
        // which they have anyway, having just typed it.
        return SharedListDto.ForMember(list);
    }
}

/// <summary>
/// Replaces a list's join code.
/// </summary>
/// <remarks>
/// The creator's alone. This is how somebody who was given the code, or passed
/// it on, stops being able to use it — so it belongs with deleting the list
/// rather than with renaming it.
/// </remarks>
public sealed record RegenerateJoinCodeCommand(Guid ListId) : IRequest<string?>;

public sealed class RegenerateJoinCodeCommandHandler(IListAccess access, IInvitationStore invitations)
    : IRequestHandler<RegenerateJoinCodeCommand, string?>
{
    public async ValueTask<string?> Handle(
        RegenerateJoinCodeCommand command,
        CancellationToken cancellationToken)
    {
        var list = await access.ForOwnerAsync(command.ListId, cancellationToken);

        return list is null
            ? null
            : await invitations.RegenerateCodeAsync(list, cancellationToken);
    }
}
