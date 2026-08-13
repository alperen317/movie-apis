using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Movie.Api;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Requests per window that may cause an email to be sent.</summary>
    public int EmailDispatchPermitLimit { get; init; } = 5;

    /// <summary>Code submissions per window.</summary>
    public int CodeSubmissionPermitLimit { get; init; } = 20;

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
    /// For code submission. The five-attempt cap on each code already protects
    /// a single account; this bounds spraying guesses across many accounts from
    /// one host, which that cap cannot see.
    /// </summary>
    public const string CodeSubmission = "code-submission";

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
            options.AddPolicy(CodeSubmission, PartitionByCaller(limits.CodeSubmissionPermitLimit, window));
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
