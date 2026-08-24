namespace Movie.Domain.Lists;

/// <summary>
/// Privilege level within a list. Only the owner can delete the list, remove
/// members and regenerate the join code; for adding and removing content,
/// members are equals.
/// </summary>
public enum MemberRole
{
    Owner,
    Member,
}