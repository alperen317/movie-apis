using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Movie.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling, so that migrations can be created
/// and applied without the API project being wired up or running.
/// </summary>
public sealed class MovieDbContextFactory : IDesignTimeDbContextFactory<MovieDbContext>
{
    /// <summary>Matches the service defined in docker-compose.yml.</summary>
    private const string LocalDevelopmentConnectionString =
        "Host=localhost;Port=5435;Database=movie;Username=movie;Password=movie_dev_password";

    public MovieDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("MOVIE_DB_CONNECTION")
            ?? LocalDevelopmentConnectionString;

        var options = new DbContextOptionsBuilder<MovieDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MovieDbContext(options);
    }
}
