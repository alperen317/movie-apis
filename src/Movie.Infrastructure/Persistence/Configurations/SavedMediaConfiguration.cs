using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Library;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class SavedMediaConfiguration : IEntityTypeConfiguration<SavedMedia>
{
    public void Configure(EntityTypeBuilder<SavedMedia> builder)
    {
        builder.ToTable("saved_media");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ListType).HasLowerCaseStringConversion().HasMaxLength(20);
        builder.Property(x => x.MediaType).HasLowerCaseStringConversion().HasMaxLength(10);
        builder.Property(x => x.Title).HasMaxLength(500);
        builder.Property(x => x.PosterPath).HasMaxLength(255);
        builder.Property(x => x.Year).HasMaxLength(20);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Backs the "my favorites, newest first" query.
        builder.HasIndex(x => new { x.UserId, x.ListType, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("saved_media_user_list_idx");

        // Lets the importer re-run without duplicating or failing: a title
        // already saved is skipped rather than inserted again.
        builder.HasIndex(x => new { x.UserId, x.ListType, x.MediaId, x.MediaType })
            .IsUnique()
            .HasDatabaseName("saved_media_user_list_media_key");
    }
}