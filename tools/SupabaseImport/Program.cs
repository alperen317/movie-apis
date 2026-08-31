// One-off tool: carries the pre-.NET Supabase project's real user data
// (accounts, shared lists, favorites/watchlist, watch log, episode progress,
// polls) into this API's database, so accounts that existed before the
// Faz 0-8 rewrite keep working afterward. See the migration plan for the
// full table-by-table mapping and why refresh_tokens/verification_codes are
// deliberately left behind.
//
// Reads two connection strings from the environment:
//   LEGACY_DB_CONNECTION -- the old Supabase project (Settings > Database)
//   TARGET_DB_CONNECTION -- this API's database (same shape as
//                           ConnectionStrings:Database in appsettings)
// Pass --force to run even if the target's `users` table already has rows
// (the default refusal exists so a second accidental run does not attempt to
// duplicate everything on top of an already-imported database).

using Microsoft.EntityFrameworkCore;

using Movie.Infrastructure.Persistence;

using Npgsql;

using SupabaseImport;

var force = args.Contains("--force");

var legacyConnectionString = RequireEnv("LEGACY_DB_CONNECTION");
var targetConnectionString = RequireEnv("TARGET_DB_CONNECTION");

await using var source = new NpgsqlConnection(legacyConnectionString);
await source.OpenAsync();

var targetOptions = new DbContextOptionsBuilder<MovieDbContext>()
    .UseNpgsql(targetConnectionString)
    .UseSnakeCaseNamingConvention()
    .Options;

await using var target = new MovieDbContext(targetOptions, new NoOpCurrentUser());

if (!force && await target.Users.AnyAsync())
{
    Console.Error.WriteLine(
        "Target database already has users. Pass --force to import anyway "
        + "(this will likely duplicate or conflict with existing rows).");
    return 1;
}

var reader = new LegacyReader(source);

await using var transaction = await target.Database.BeginTransactionAsync();

await ImportAsync("users", reader.ReadUsersAsync, target.Users);
await ImportAsync("lists", reader.ReadListsAsync, target.Lists);
await ImportAsync("list_members", reader.ReadListMembersAsync, target.ListMembers);
await ImportAsync("list_items", reader.ReadListItemsAsync, target.ListItems);
await ImportAsync("list_polls", reader.ReadListPollsAsync, target.ListPolls);
await ImportAsync("list_poll_candidates", reader.ReadListPollCandidatesAsync, target.ListPollCandidates);
await ImportAsync("list_poll_votes", reader.ReadListPollVotesAsync, target.ListPollVotes);
await ImportAsync("saved_media", reader.ReadSavedMediaAsync, target.SavedMedia);
await ImportAsync("watch_log", reader.ReadWatchLogAsync, target.WatchLog);
await ImportAsync("episode_progress", reader.ReadEpisodeProgressAsync, target.EpisodeProgress);
await ImportAsync("recommendation_feedback", reader.ReadRecommendationFeedbackAsync, target.RecommendationFeedback);

await transaction.CommitAsync();

Console.WriteLine();
Console.WriteLine("Import complete.");

await RunIntegrityChecksAsync(target);

return 0;

async Task ImportAsync<T>(string label, Func<CancellationToken, Task<List<T>>> read, DbSet<T> destination)
    where T : class
{
    var rows = await read(CancellationToken.None);
    await destination.AddRangeAsync(rows);
    await target.SaveChangesAsync();
    Console.WriteLine($"{label,-28} {rows.Count,6} rows");
}

static string RequireEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} environment variable is not set.");

/// <summary>
/// Cheap sanity checks after the fact rather than a guarantee: every foreign
/// key the import wrote should resolve, since both sides came from the same
/// snapshot, but a mismatch here is exactly the kind of thing worth catching
/// before anyone tries to sign in.
/// </summary>
static async Task RunIntegrityChecksAsync(MovieDbContext db)
{
    Console.WriteLine();
    Console.WriteLine("Integrity checks:");

    await CheckAsync(
        "list_members without a matching user",
        db.ListMembers.Where(member => !db.Users.Any(user => user.Id == member.UserId)).CountAsync());

    await CheckAsync(
        "list_items without a matching list",
        db.ListItems.Where(item => !db.Lists.Any(list => list.Id == item.ListId)).CountAsync());

    await CheckAsync(
        "poll votes without a matching candidate",
        db.ListPollVotes.Where(vote => !db.ListPollCandidates.Any(c => c.Id == vote.CandidateId)).CountAsync());

    static async Task CheckAsync(string label, Task<int> count)
    {
        var found = await count;
        Console.WriteLine(found == 0 ? $"  OK   {label}" : $"  FAIL {label}: {found}");
    }
}