using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// Boots the real application with configuration supplied here rather than from
/// user-secrets, so the tests behave the same on a machine that has never run
/// <c>dotnet user-secrets</c> — a build server, for instance.
/// </summary>
public sealed class MovieApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "movie-api-tests";

    public const string Audience = "movie-app-tests";

    /// <summary>Only needs to clear the 32-byte floor HMAC-SHA256 requires.</summary>
    public const string SigningKey = "integration-test-signing-key-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting rather than ConfigureAppConfiguration: under the minimal
        // hosting model Program.cs reads configuration while the host is being
        // built, which is before the ConfigureAppConfiguration callbacks run.
        // These land in host configuration, which is in place from the start.

        // Npgsql opens no connection until something queries, and these tests
        // only exercise the authentication pipeline. Phase 2f swaps this for a
        // Testcontainers database.
        builder.UseSetting(
            "ConnectionStrings:Database",
            "Host=localhost;Port=5435;Database=movie;Username=movie;Password=unused");

        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
    }
}
