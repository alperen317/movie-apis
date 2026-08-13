namespace Movie.Domain.Users;

/// <summary>
/// What a verification code is good for. A code issued for one purpose must
/// never satisfy the other: a code emailed to confirm an address should not be
/// usable to take over that account through the reset flow.
/// </summary>
public enum CodePurpose
{
    EmailConfirmation,
    PasswordReset,
}
