using Movie.Domain.Lists;

namespace Movie.Application.Features.Lists;

/// <param name="JoinCode">
/// Null for someone who has been invited but has not accepted. Holding the code
/// is enough to join a list outright, with no invitation and nobody's approval,
/// which makes it an authorization token rather than a label — see
/// <see cref="JoinCodeGenerator"/> for why it is generated the way it is.
/// Supabase handed it to pending invitees too, because its row policy on
/// <c>lists</c> could say which rows were readable but not which columns.
/// </param>
public sealed record SharedListDto(
    Guid Id,
    string Name,
    Guid CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? JoinCode)
{
    /// <summary>Everything, for someone who has joined.</summary>
    public static SharedListDto ForMember(MediaList list) => new(
        list.Id,
        list.Name,
        list.CreatedById,
        list.CreatedAt,
        list.UpdatedAt,
        list.JoinCode);

    /// <summary>
    /// Enough to render "Alice invited you to Oscar Winners", and no more. The
    /// invitation card wants the name; it has no use for a code that would let
    /// its holder skip the invitation entirely.
    /// </summary>
    public static SharedListDto ForInvitee(MediaList list) => new(
        list.Id,
        list.Name,
        list.CreatedById,
        list.CreatedAt,
        list.UpdatedAt,
        JoinCode: null);
}