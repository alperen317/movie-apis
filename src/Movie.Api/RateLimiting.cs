using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Movie.Api;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per window that may cause an email to be sent.</summary>
    public int EmailDispatchPermitLimit { get; init; } = 5;

    /// <summary>Password and code submissions per window.</summary>
    public int CredentialSubmissionPermitLimit { get; init; } = 20;

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
}
