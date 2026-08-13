using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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
public sealed class MovieDbContext(DbContextOptions<MovieDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<VerificationCode> VerificationCodes => Set<VerificationCode>();

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
