using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movie.Domain.Lists;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class ListItemConfiguration : IEntityTypeConfiguration<ListItem>
{
    public void Configure(EntityTypeBuilder<ListItem> builder)
    {
        builder.ToTable("list_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MediaType).HasLowerCaseStringConversion().HasMaxLength(10);
        builder.Property(x => x.Title).HasMaxLength(500);
        builder.Property(x => x.PosterPath).HasMaxLength(255);
        builder.Property(x => x.Year).HasMaxLength(20);

        builder.Property(x => x.AddedById).HasColumnName("added_by");

        builder.HasOne(x => x.AddedBy)
            .WithMany()
            .HasForeignKey(x => x.AddedById)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ListId, x.MediaId, x.MediaType })
            .IsUnique()
            .HasDatabaseName("list_items_list_media_key");

        builder.HasIndex(x => new { x.ListId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("list_items_list_idx");
    }
}
