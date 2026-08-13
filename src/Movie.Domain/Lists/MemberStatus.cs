namespace Movie.Domain.Lists;

/// <summary>
/// State of an invitation. Membership and invitations are not separate tables:
/// a pending invite is simply a membership row in the <see cref="Pending"/>
/// state.
/// </summary>
public enum MemberStatus
{
    Pending,
    Accepted,
    Declined,
}
