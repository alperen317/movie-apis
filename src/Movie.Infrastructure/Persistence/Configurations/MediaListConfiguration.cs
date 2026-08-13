using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movie.Domain.Lists;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class MediaListConfiguration : IEntityTypeConfiguration<MediaList>
{
    public void Configure(EntityTypeBuilder<MediaList> builder)
    {
        builder.ToTable("lists", t =>
            t.HasCheckConstraint("lists_name_length", "char_length(btrim(name)) between 1 and 60"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(60);

        // Deliberately varchar, not char(8): char pads and ignores trailing
        // whitespace when comparing, which is the wrong semantics for matching
        // a code someone typed.
        builder.Property(x => x.JoinCode).HasMaxLength(JoinCodeGenerator.Length);

        builder.Property(x => x.CreatedById).HasColumnName("created_by");

        builder.HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Members)
            .WithOne(x => x.List)
            .HasForeignKey(x => x.ListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Items)
            .WithOne(x => x.List)
            .HasForeignKey(x => x.ListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Polls)
            .WithOne(x => x.List)
            .HasForeignKey(x => x.ListId)
            .OnDelete(DeleteBehavior.Cascade);

        // Joining is a lookup by code alone, so this index carries that whole
        // path — and the uniqueness is what makes a code identify one list.
        builder.HasIndex(x => x.JoinCode).IsUnique().HasDatabaseName("lists_join_code_key");

        builder.HasIndex(x => x.CreatedById).HasDatabaseName("lists_created_by_idx");
    }
}
