using Movie.Domain.Users;

namespace Movie.Application.Abstractions.Authentication;

/// <summary>
/// Issues and redeems the six-digit codes used for email confirmation and
/// password reset. Endpoints depend on this rather than on any particular code
/// scheme, so the implementation can change without touching them.
/// </summary>
public interface IVerificationCodeService
{
    /// <summary>
    /// Produces a fresh code and invalidates any earlier one for the same
    /// purpose, so "resend" leaves exactly one code alive.
    /// </summary>
    /// <returns>The plain code, to be emailed. It is not stored in this form.</returns>
    Task<string> IssueAsync(ApplicationUser user, CodePurpose purpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a submitted code and, on success, marks it used so it cannot be
    /// replayed. Callers must treat this as the authorization step and perform
    /// the state change it guards in the same request.
    /// </summary>
    Task<VerificationResult> ConsumeAsync(
        ApplicationUser user,
        CodePurpose purpose,
        string code,
        CancellationToken cancellationToken = default);
}

public enum VerificationResult
{
    Success,

    /// <summary>No live code, or the digits do not match.</summary>
    Invalid,

    /// <summary>
    /// Reported separately from <see cref="Invalid"/> so the client can say
    /// "request a new code" instead of "wrong code". It reveals nothing extra:
    /// reaching this point already requires knowing the account's email.
    /// </summary>
    Expired,

    TooManyAttempts,
}
