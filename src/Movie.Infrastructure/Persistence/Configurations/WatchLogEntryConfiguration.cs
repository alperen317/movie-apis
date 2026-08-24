using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Library;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class WatchLogEntryConfiguration : IEntityTypeConfiguration<WatchLogEntry>
{
    public void Configure(EntityTypeBuilder<WatchLogEntry> builder)
    {
        builder.ToTable("watch_log", t =>
            t.HasCheckConstraint("watch_log_rating_range", "rating is null or rating between 1 and 10"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MediaType).HasLowerCaseStringConversion().HasMaxLength(10);
        builder.Property(x => x.Title).HasMaxLength(500);
        builder.Property(x => x.PosterPath).HasMaxLength(255);
        builder.Property(x => x.Year).HasMaxLength(20);
        builder.Property(x => x.Rating).HasColumnType("smallint");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The diary screen: everything I watched, newest first.
        builder.HasIndex(x => new { x.UserId, x.WatchedAt })
            .IsDescending(false, true)
            .HasDatabaseName("watch_log_user_watched_idx");

        // "Have I seen this?" for one title — no uniqueness, since rewatches
        // deliberately produce several rows.
        builder.HasIndex(x => new { x.UserId, x.MediaType, x.MediaId })
            .HasDatabaseName("watch_log_user_media_idx");
    }
}