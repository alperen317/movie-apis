using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Movie.Application.Abstractions;
using Movie.Domain.Library;
using Movie.Domain.Lists;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence;

/// <summary>
/// Derives from <see cref="IdentityUserContext{TUser,TKey}"/> rather than
/// <c>IdentityDbContext</c> because the app has no global role concept —
/// <see cref="MemberRole"/> is scoped to a single list and has nothing to do
/// with Identity roles. That keeps three permanently empty tables out of the
/// schema; role support is a later migration if it is ever needed.
/// </summary>
public sealed class MovieDbContext(DbContextOptions<MovieDbContext> options, ICurrentUser currentUser)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    /// <summary>
    /// Read at query time rather than baked into the model, so one context can
    /// serve one request's user and the next another's.
    /// </summary>
    private Guid? CurrentUserId => currentUser.Id;

    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<SavedMedia> SavedMedia => Set<SavedMedia>();

    public DbSet<WatchLogEntry> WatchLog => Set<WatchLogEntry>();

    public DbSet<EpisodeProgress> EpisodeProgress => Set<EpisodeProgress>();

    public DbSet<RecommendationFeedback> RecommendationFeedback => Set<RecommendationFeedback>();

    public DbSet<MediaList> Lists => Set<MediaList>();

    public DbSet<ListMember> ListMembers => Set<ListMember>();

    public DbSet<ListItem> ListItems => Set<ListItem>();

    public DbSet<ListPoll> ListPolls => Set<ListPoll>();

    public DbSet<ListPollCandidate> ListPollCandidates => Set<ListPollCandidate>();

    public DbSet<ListPollVote> ListPollVotes => Set<ListPollVote>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity's own tables ship as AspNetUsers, AspNetUserClaims and so on.
        // Renamed here so the whole schema reads in one voice next to lists,
        // saved_media and the rest. ApplicationUser is renamed in its own
        // configuration class, alongside the rest of its mapping.
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(MovieDbContext).Assembly);

        ApplyOwnershipFilters(builder);
    }

    /// <summary>
    /// Stands in for the row-level security that used to scope these tables to
    /// their owner. Applied on the model rather than written into each query,
    /// because the thing being replaced could not be forgotten and neither
    /// should this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the four tables that hold a user's own content. Deliberately not
    /// <c>verification_codes</c> or <c>refresh_tokens</c>: those are read while
    /// signing in, when there is no current user yet, so a filter would match
    /// nothing and break the very flow that establishes who the caller is.
    /// </para>
    /// <para>
    /// A null <see cref="CurrentUserId"/> matches no rows, so an unauthenticated
    /// context sees nothing rather than everything.
    /// </para>
    /// <para>
    /// Reading another member's rows is still legitimate in one place — the
    /// per-title watched count on a shared list, which reports an aggregate and
    /// never individual entries. That query opts out with
    /// <c>IgnoreQueryFilters()</c>, which makes the exception visible at the
    /// call site instead of leaving the rule vague.
    /// </para>
    /// </remarks>
    private void ApplyOwnershipFilters(ModelBuilder builder)
    {
        builder.Entity<SavedMedia>().HasQueryFilter(x => x.UserId == CurrentUserId);
        builder.Entity<WatchLogEntry>().HasQueryFilter(x => x.UserId == CurrentUserId);
        builder.Entity<EpisodeProgress>().HasQueryFilter(x => x.UserId == CurrentUserId);
        builder.Entity<RecommendationFeedback>().HasQueryFilter(x => x.UserId == CurrentUserId);
    }

    public override int SaveChanges()
    {
        TouchUpdatedAt();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        TouchUpdatedAt();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Stands in for Supabase's <c>touch_updated_at</c> trigger. It lives here
    /// rather than in a handler so no write path can forget it.
    /// </summary>
    private void TouchUpdatedAt()
    {
        foreach (var entry in ChangeTracker.Entries<MediaList>())
        {
            if (entry.State is EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
}
