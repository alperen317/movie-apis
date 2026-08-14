using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Movie.Infrastructure.Persistence;

/// <summary>
/// Tells a broken unique index apart from every other reason a write can fail,
/// and cleans up after one so the write can be tried again.
/// </summary>
/// <remarks>
/// Several tables here treat a duplicate as "already done" rather than as an
/// error, and letting the database decide that is what makes it race-free. The
/// alternative — checking first — narrows the window without closing it.
/// </remarks>
internal static class UniqueViolations
{
    /// <summary>Postgres' <c>unique_violation</c>.</summary>
    private const string SqlState = "23505";

    public static bool Caused(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: SqlState };

    /// <summary>
    /// Drops the rows a failed insert left tracked, so a retry starts from what
    /// the database holds rather than from what the first attempt hoped for.
    /// </summary>
    public static void ForgetPendingInserts<TEntity>(this DbContext context)
        where TEntity : class
    {
        var pending = context.ChangeTracker
            .Entries<TEntity>()
            .Where(entry => entry.State is EntityState.Added)
            .ToList();

        foreach (var entry in pending)
        {
            entry.State = EntityState.Detached;
        }
    }
}
