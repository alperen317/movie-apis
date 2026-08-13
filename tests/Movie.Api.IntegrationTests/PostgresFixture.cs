using Microsoft.EntityFrameworkCore;
using Movie.Infrastructure.Authentication;
using Movie.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// A throwaway Postgres for the tests that genuinely need one. It is a real
/// server, not an in-memory stand-in, because the behaviour under test includes
/// things only Postgres decides: cascade deletes, unique violations and the SQL
/// EF generates for bulk deletes.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Pinned to the same image docker-compose runs.</summary>
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Migrate rather than EnsureCreated, so the tests run against the same
        // schema a deployment would produce.
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    /// <param name="actingAs">
    /// Whose rows the context may see. Left out, it sees none of the
    /// user-owned tables — which is the safe default, and what an
    /// unauthenticated request gets.
    /// </param>
    public MovieDbContext CreateContext(Guid? actingAs = null) =>
        new(
            new DbContextOptionsBuilder<MovieDbContext>()
                .UseNpgsql(ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options,
            new StaticCurrentUser(actingAs));

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
