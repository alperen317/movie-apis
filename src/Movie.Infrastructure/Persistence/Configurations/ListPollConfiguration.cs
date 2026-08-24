using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Lists;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class ListPollConfiguration : IEntityTypeConfiguration<ListPoll>
{
    public void Configure(EntityTypeBuilder<ListPoll> builder)
    {
        builder.ToTable("list_polls");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CreatedById).HasColumnName("created_by");

        builder.HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedById)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Candidates)
            .WithOne(x => x.Poll)
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        // Reading a list's poll means "the most recent one", so the ordering
        // lives in the index.
        builder.HasIndex(x => new { x.ListId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("list_polls_list_idx");
    }
}