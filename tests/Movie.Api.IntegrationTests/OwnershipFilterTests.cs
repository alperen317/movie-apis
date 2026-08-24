using Microsoft.EntityFrameworkCore;

using Movie.Domain.Library;
using Movie.Domain.Media;
using Movie.Domain.Users;

using Shouldly;

namespace Movie.Api.IntegrationTests;

/// <summary>
/// The replacement for the row-level security that used to scope these tables.
/// Each test writes rows for two people and checks that one of them cannot
/// reach the other's, which is the property Supabase enforced in the database.
/// </summary>
public sealed class OwnershipFilterTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Saved_media_is_only_visible_to_its_owner()
    {
        var (mine, theirs) = await TwoUsersWithSavedMediaAsync();

        await using var context = postgres.CreateContext(actingAs: mine);
        var visible = await context.SavedMedia.ToListAsync();

        visible.ShouldAllBe(x => x.UserId == mine);
        visible.ShouldNotBeEmpty();
        visible.ShouldAllBe(x => x.UserId != theirs);
    }

    [Fact]
    public async Task Another_persons_row_cannot_be_fetched_even_by_its_id()
    {
        var (mine, theirs) = await TwoUsersWithSavedMediaAsync();

        Guid theirRowId;
        await using (var asThem = postgres.CreateContext(actingAs: theirs))
        {
            theirRowId = (await asThem.SavedMedia.SingleAsync()).Id;
        }

        await using var asMe = postgres.CreateContext(actingAs: mine);

        // Knowing the identifier is not access. This is the case a forgotten
        // WHERE clause would otherwise let through.
        (await asMe.SavedMedia.SingleOrDefaultAsync(x => x.Id == theirRowId)).ShouldBeNull();
    }

    [Fact]
    public async Task A_context_with_nobody_signed_in_sees_nothing()
    {
        await TwoUsersWithSavedMediaAsync();

        await using var anonymous = postgres.CreateContext();

        // Failing closed matters more than failing loudly: an unauthenticated
        // context must not fall back to seeing everything.
        (await anonymous.SavedMedia.AnyAsync()).ShouldBeFalse();
        (await anonymous.WatchLog.AnyAsync()).ShouldBeFalse();
        (await anonymous.EpisodeProgress.AnyAsync()).ShouldBeFalse();
        (await anonymous.RecommendationFeedback.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Every_personal_table_is_scoped()
    {
        var mine = await CreateUserAsync();
        var theirs = await CreateUserAsync();

        await using (var seed = postgres.CreateContext())
        {
            seed.WatchLog.AddRange(WatchEntry(mine), WatchEntry(theirs));
            seed.EpisodeProgress.AddRange(Episode(mine), Episode(theirs));
            seed.RecommendationFeedback.AddRange(Feedback(mine), Feedback(theirs));
            await seed.SaveChangesAsync();
        }

        await using var context = postgres.CreateContext(actingAs: mine);

        (await context.WatchLog.CountAsync()).ShouldBe(1);
        (await context.EpisodeProgress.CountAsync()).ShouldBe(1);
        (await context.RecommendationFeedback.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Opting_out_is_how_a_legitimate_cross_user_read_happens()
    {
        var mine = await CreateUserAsync();
        var theirs = await CreateUserAsync();

        await using (var seed = postgres.CreateContext())
        {
            seed.WatchLog.AddRange(WatchEntry(mine), WatchEntry(theirs));
            await seed.SaveChangesAsync();
        }

        await using var context = postgres.CreateContext(actingAs: mine);

        // The shared-list watched count needs other members' rows. It reports
        // an aggregate and never an individual entry, and saying so out loud at
        // the call site is what keeps the rule from quietly eroding.
        var everyone = await context.WatchLog.IgnoreQueryFilters().CountAsync();

        everyone.ShouldBeGreaterThanOrEqualTo(2);
        (await context.WatchLog.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Writes_are_not_filtered_so_seeding_still_works()
    {
        var mine = await CreateUserAsync();

        await using var context = postgres.CreateContext(actingAs: mine);
        context.SavedMedia.Add(Saved(mine));
        await context.SaveChangesAsync();

        // The filter shapes reads. Inserting for the acting user is ordinary
        // work and must not be affected.
        (await context.SavedMedia.CountAsync()).ShouldBe(1);
    }

    private async Task<(Guid Mine, Guid Theirs)> TwoUsersWithSavedMediaAsync()
    {
        var mine = await CreateUserAsync();
        var theirs = await CreateUserAsync();

        await using var seed = postgres.CreateContext();
        seed.SavedMedia.AddRange(Saved(mine), Saved(theirs));
        await seed.SaveChangesAsync();

        return (mine, theirs);
    }

    private static SavedMedia Saved(Guid userId) => new()
    {
        UserId = userId,
        ListType = ListType.Favorite,
        MediaId = Random.Shared.Next(1, 1_000_000),
        MediaType = MediaType.Movie,
        Title = "Fight Club",
    };

    private static WatchLogEntry WatchEntry(Guid userId) => new()
    {
        UserId = userId,
        MediaId = Random.Shared.Next(1, 1_000_000),
        MediaType = MediaType.Movie,
        Title = "Fight Club",
        WatchedAt = DateTime.UtcNow,
    };

    private static EpisodeProgress Episode(Guid userId) => new()
    {
        UserId = userId,
        ShowId = Random.Shared.Next(1, 1_000_000),
        SeasonNumber = 1,
        EpisodeNumber = 1,
    };

    private static RecommendationFeedback Feedback(Guid userId) => new()
    {
        UserId = userId,
        MediaId = Random.Shared.Next(1, 1_000_000),
        MediaType = MediaType.Tv,
    };

    private async Task<Guid> CreateUserAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
        };
        user.UserName = user.Email;
        user.NormalizedEmail = user.Email!.ToUpperInvariant();
        user.NormalizedUserName = user.NormalizedEmail;
        user.SecurityStamp = Guid.NewGuid().ToString();

        await using var context = postgres.CreateContext();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return user.Id;
    }
}