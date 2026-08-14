using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Movie.Api;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per window that may cause an email to be sent.</summary>
    public int EmailDispatchPermitLimit { get; init; } = 5;

    /// <summary>Password and code submissions per window.</summary>
    public int CredentialSubmissionPermitLimit { get; init; } = 20;

    /// <summary>Invitations one account may send per window.</summary>
    public int ListInvitationPermitLimit { get; init; } = 20;

    /// <summary>Join codes one account may try per window.</summary>
    public int JoinAttemptPermitLimit { get; init; } = 20;

    public int WindowMinutes { get; init; } = 10;
}

public static class RateLimiting
{
    /// <summary>
    /// For endpoints that cause an email to be sent. Abuse here costs money and
    /// lands in someone else's inbox, so it is the tighter of the two.
    /// </summary>
    public const string EmailDispatch = "email-dispatch";

    /// <summary>
    /// For anything that submits a secret — a password or a code. Per-account
    /// defences already exist on both paths (five attempts per code, lockout
    /// after ten bad passwords); this bounds spraying guesses across many
    /// accounts from one host, which those cannot see.
    /// </summary>
    public const string CredentialSubmission = "credential-submission";

    /// <summary>
    /// For sending invitations. Bounds two things at once: an inbox somebody
    /// else owns, and the rate at which addresses can be probed. The endpoint
    /// answers the same way whether or not an address has an account, so what
    /// is left to an attacker is volume — and this is what takes that away.
    /// </summary>
    public const string ListInvitation = "list-invitation";

    /// <summary>
    /// For joining by code. A code is worth guessing: it grants membership on
    /// its own, with nobody's approval. Eight symbols from an alphabet of 32
    /// make guessing hopeless anyway, but only while the attempts are counted.
    /// </summary>
    public const string JoinAttempt = "join-attempt";

    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
            ?? new RateLimitOptions();

        var window = TimeSpan.FromMinutes(limits.WindowMinutes);

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(EmailDispatch, PartitionByCaller(limits.EmailDispatchPermitLimit, window));
            options.AddPolicy(
                CredentialSubmission,
                PartitionByCaller(limits.CredentialSubmissionPermitLimit, window));

            // These two are behind authentication, so there is something better
            // to count than a host — see PartitionByAccount.
            options.AddPolicy(
                ListInvitation,
                PartitionByAccount(limits.ListInvitationPermitLimit, window));
            options.AddPolicy(
                JoinAttempt,
                PartitionByAccount(limits.JoinAttemptPermitLimit, window));
        });
    }

    /// <summary>
    /// Partitions on the caller's address. Deliberately not on the submitted
    /// email: that value is attacker-chosen, so anyone could sidestep the limit
    /// by varying it.
    /// </summary>
    private static Func<HttpContext, RateLimitPartition<string>> PartitionByCaller(
        int permitLimit,
        TimeSpan window) =>
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
            });

    /// <summary>
    /// Partitions on the signed-in account, which is what the Supabase
    /// equivalents counted.
    /// </summary>
    /// <remarks>
    /// Better than a host wherever the caller has to be signed in: an address
    /// is cheap to change and shared by everyone behind one router, so counting
    /// it both under-restricts the attacker and over-restricts everyone else.
    /// An unauthenticated request cannot reach these endpoints at all, so the
    /// fallback exists only so the partition key is never empty.
    /// </remarks>
    private static Func<HttpContext, RateLimitPartition<string>> PartitionByAccount(
        int permitLimit,
        TimeSpan window) =>
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
            });
}
