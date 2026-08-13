using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movie.Domain.Library;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class EpisodeProgressConfiguration : IEntityTypeConfiguration<EpisodeProgress>
{
    public void Configure(EntityTypeBuilder<EpisodeProgress> builder)
    {
        builder.ToTable("episode_progress");

        // No surrogate key: the tuple itself identifies the row, which is what
        // makes "mark everything up to here" a plain multi-row upsert.
        builder.HasKey(x => new { x.UserId, x.ShowId, x.SeasonNumber, x.EpisodeNumber });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.ShowId })
            .HasDatabaseName("episode_progress_user_show_idx");
    }
}
