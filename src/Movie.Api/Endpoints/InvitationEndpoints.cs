using System.ComponentModel.DataAnnotations;

using Mediator;

using Microsoft.AspNetCore.RateLimiting;

using Movie.Application.Abstractions.Lists;
using Movie.Application.Features.Lists;

namespace Movie.Api.Endpoints;

/// <summary>
/// Getting into a shared list: by invitation, or by knowing its code.
/// </summary>
/// <remarks>
/// The most carefully guarded group in the API, because two of its answers are
/// worth harvesting: whether an address has an account, and whether a code
/// names a list. Both are throttled, and the first is deliberately unanswerable
/// — see <see cref="IInvitationStore"/>.
/// </remarks>
public static class InvitationEndpoints
{
    public static void MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        var lists = app.MapGroup("/lists").RequireAuthorization().WithTags("Invitations");

        // Ahead of /lists/{id:guid} only in the reading; the guid constraint is
        // what actually keeps the literal routes apart from it.
        lists.MapGet("/invites", GetPendingInvites);
        lists.MapPost("/join", JoinByCode).RequireRateLimiting(RateLimiting.JoinAttempt);
        lists.MapPost("/{id:guid}/invites", Invite)
            .RequireRateLimiting(RateLimiting.ListInvitation);
        lists.MapPost("/{id:guid}/join-code", RegenerateJoinCode);

        var invites = app.MapGroup("/invites").RequireAuthorization().WithTags("Invitations");

        invites.MapPost("/{membershipId:guid}/response", Respond);
    }

    /// <summary>
    /// Answers the same way for an address with no account and one already
    /// invited or already a member.
    /// </summary>
    private static async Task<IResult> Invite(Guid id, InviteRequest request, ISender sender)
    {
        var email = request.Email?.Trim();

        if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["email"] = ["A valid email address is required."],
            });
        }

        var response = await sender.Send(new InviteToListCommand(id, email));

        return response.Outcome switch
        {
            InviteOutcome.Invited => Results.Ok(response.Membership),

            // One answer covering "no account here" and "already on the list".
            // Told apart, they would let the owner of a list they control probe
            // addresses one at a time to learn which are registered.
            InviteOutcome.Failed => Results.Conflict(new
            {
                error = "invite_failed",
                message = "Couldn't send that invite — double-check the email.",
            }),

            // Safe to keep separate: it only ever fires for the caller's own
            // address, so it says nothing about anybody else.
            InviteOutcome.CannotInviteSelf => Results.Conflict(new
            {
                error = "cannot_invite_self",
                message = "You can't invite yourself.",
            }),

            _ => Results.NotFound(),
        };
    }

    private static async Task<IResult> GetPendingInvites(ISender sender) =>
        Results.Ok(await sender.Send(new GetPendingInvitesQuery()));

    private static async Task<IResult> Respond(
        Guid membershipId,
        RespondRequest request,
        ISender sender)
    {
        var answered = await sender.Send(
            new RespondToInviteCommand(membershipId, request.Accept));

        // An invitation that is not the caller's, or has already been answered,
        // is not there — 404 rather than an error that confirms it exists.
        return answered ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> JoinByCode(JoinRequest request, ISender sender)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["A code is required."],
            });
        }

        var list = await sender.Send(new JoinListByCodeCommand(request.Code));

        return list is null
            ? Results.NotFound(new
            {
                error = "invalid_code",
                message = "That code doesn't match any list.",
            })
            : Results.Ok(list);
    }

    private static async Task<IResult> RegenerateJoinCode(Guid id, ISender sender)
    {
        var code = await sender.Send(new RegenerateJoinCodeCommand(id));

        return code is null
            ? Results.NotFound()
            : Results.Ok(new JoinCodeResponse(code));
    }

    public sealed record InviteRequest(string? Email);

    public sealed record RespondRequest(bool Accept);

    public sealed record JoinRequest(string? Code);

    public sealed record JoinCodeResponse(string JoinCode);
}