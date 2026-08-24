using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Movie.Application.Abstractions.Email;
using Movie.Infrastructure.Authentication;
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

    /// <summary>What <see cref="SignedInAsync"/> registers accounts with.</summary>
    public const string DefaultPassword = "correct horse battery";

    private readonly PostgreSqlContainer _database =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>
    /// Overridden by the class that exercises throttling. Everywhere else the
    /// limits are raised out of the way, because every test shares one loopback
    /// address and would otherwise spend a single budget between them.
    /// </summary>
    protected virtual int EmailDispatchPermitLimit => 1000;

    protected virtual int CredentialSubmissionPermitLimit => 1000;

    protected virtual int ListInvitationPermitLimit => 1000;

    protected virtual int JoinAttemptPermitLimit => 1000;

    public async Task InitializeAsync()
    {
        await _database.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// A client holding a bearer token for a brand new, verified account.
    /// </summary>
    /// <remarks>
    /// Goes through register and verify rather than writing the rows, so what
    /// the tests are handed is a token the application itself issued. A fresh
    /// account each time keeps one test's content out of another's.
    /// </remarks>
    public async Task<SignedInUser> SignedInAsync()
    {
        var setup = CreateClient();
        var email = $"{Guid.NewGuid():N}@example.com";

        await setup.PostAsJsonAsync("/auth/register", new { email, password = DefaultPassword });
        var verified = await setup.PostAsJsonAsync(
            "/auth/verify-email",
            new { email, code = Emails.CodeSentTo(email) });

        var tokens = await verified.Content.ReadFromJsonAsync<IssuedTokens>();

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var profile = await client.GetFromJsonAsync<SignedInProfile>("/me");

        return new SignedInUser(client, email) { Id = profile!.Id };
    }

    /// <remarks>
    /// The id is a property rather than a fourth positional member, so tests
    /// that only want the client and the address keep deconstructing into two.
    /// </remarks>
    public sealed record SignedInUser(HttpClient Client, string Email)
    {
        public required Guid Id { get; init; }
    }

    private sealed record SignedInProfile(Guid Id);

    private sealed record IssuedTokens(string AccessToken, DateTime ExpiresAt, string RefreshToken);

    /// <param name="actingAs">
    /// Whose rows the context may see. Left out, it sees none of the
    /// user-owned tables.
    /// </param>
    public MovieDbContext CreateContext(Guid? actingAs = null) =>
        new(
            new DbContextOptionsBuilder<MovieDbContext>()
                .UseNpgsql(_database.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options,
            new StaticCurrentUser(actingAs));

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
            "RateLimiting:CredentialSubmissionPermitLimit",
            CredentialSubmissionPermitLimit.ToString());
        builder.UseSetting(
            "RateLimiting:ListInvitationPermitLimit",
            ListInvitationPermitLimit.ToString());
        builder.UseSetting(
            "RateLimiting:JoinAttemptPermitLimit",
            JoinAttemptPermitLimit.ToString());

        builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IEmailSender>(_ => Emails)));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _database.DisposeAsync();
    }
}