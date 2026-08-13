using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
        GuardTamperableColumns();
        TouchUpdatedAt();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardTamperableColumns();
        TouchUpdatedAt();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Stands in for the two tamper-guard triggers.
    /// </summary>
    /// <remarks>
    /// Here rather than in the handlers, for the reason the originals were
    /// triggers: a rule that only holds where someone remembered to write it
    /// does not hold. Every write goes through this method.
    /// </remarks>
    private void GuardTamperableColumns()
    {
        foreach (var entry in ChangeTracker.Entries<MediaList>())
        {
            if (entry.State is not EntityState.Modified)
            {
                continue;
            }

            // The update rule says which rows may change, never which columns.
            // Without this, anyone allowed to rename a list could rewrite its
            // creator in the same statement and take it over.
            var createdBy = entry.Property(list => list.CreatedById);
            if (!Equals(createdBy.OriginalValue, createdBy.CurrentValue))
            {
                throw new InvalidOperationException(
                    "A list's creator cannot be changed.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<ListMember>())
        {
            if (entry.State is not EntityState.Modified)
            {
                continue;
            }

            GuardMembership(entry);
        }
    }

    private static void GuardMembership(EntityEntry<ListMember> entry)
    {
        // Left free, a member could repoint their own row at a list they were
        // never invited to and mark it accepted.
        foreach (var name in (string[])[nameof(ListMember.ListId), nameof(ListMember.UserId), nameof(ListMember.Role)])
        {
            var property = entry.Property(name);
            if (!Equals(property.OriginalValue, property.CurrentValue))
            {
                throw new InvalidOperationException(
                    $"A membership's {name} cannot be changed.");
            }
        }

        var status = entry.Property(member => member.Status);
        var from = (MemberStatus)status.OriginalValue;
        var to = (MemberStatus)status.CurrentValue;

        if (from == to)
        {
            return;
        }

        // Asked of the entity as it was loaded, so the rule lives in the domain
        // and can be reasoned about without a database.
        var asLoaded = new ListMember
        {
            ListId = entry.Entity.ListId,
            UserId = entry.Entity.UserId,
            Status = from,
        };

        if (!asLoaded.CanTransitionTo(to))
        {
            throw new InvalidOperationException(
                $"A membership cannot go from {from} to {to}.");
        }
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
