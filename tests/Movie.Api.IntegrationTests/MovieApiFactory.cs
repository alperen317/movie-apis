using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Movie.Application.Abstractions.Email;
using Movie.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Runs the real application against a throwaway Postgres.
/// </summary>
/// <remarks>
/// Configuration is supplied here rather than read from user-secrets, so the
/// tests behave the same on a machine that has never run
/// <c>dotnet user-secrets</c> — a build server, for instance.
/// </remarks>
public class MovieApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "movie-api-tests";

    public const string Audience = "movie-app-tests";

    /// <summary>Only needs to clear the 32-byte floor HMAC-SHA256 requires.</summary>
    public const string SigningKey = "integration-test-signing-key-0123456789";

    private readonly PostgreSqlContainer _database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>
    /// Overridden by the class that exercises throttling. Everywhere else the
    /// limits are raised out of the way, because every test shares one loopback
    /// address and would otherwise spend a single budget between them.
    /// </summary>
    protected virtual int EmailDispatchPermitLimit => 1000;

    protected virtual int CodeSubmissionPermitLimit => 1000;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public MovieDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MovieDbContext>()
            .UseNpgsql(_database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting rather than ConfigureAppConfiguration: under the minimal
        // hosting model Program.cs reads configuration while the host is being
        // built, which is before the ConfigureAppConfiguration callbacks run.
        builder.UseSetting("ConnectionStrings:Database", _database.GetConnectionString());
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting(
            "RateLimiting:EmailDispatchPermitLimit",
            EmailDispatchPermitLimit.ToString());
        builder.UseSetting(
            "RateLimiting:CodeSubmissionPermitLimit",
            CodeSubmissionPermitLimit.ToString());

        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IEmailSender>(_ => Emails)));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _database.DisposeAsync();
    }
}
