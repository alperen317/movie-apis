using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Lists;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class ListPollCandidateConfiguration : IEntityTypeConfiguration<ListPollCandidate>
{
    public void Configure(EntityTypeBuilder<ListPollCandidate> builder)
    {
        builder.ToTable("list_poll_candidates");

        builder.HasKey(x => x.Id);

        // Removing a title from the list also withdraws its candidacy, so a
        // poll never shows something that is no longer there.
        builder.HasOne(x => x.ListItem)
            .WithMany()
            .HasForeignKey(x => x.ListItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Votes)
            .WithOne(x => x.Candidate)
            .HasForeignKey(x => x.CandidateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.PollId, x.ListItemId })
            .IsUnique()
            .HasDatabaseName("list_poll_candidates_poll_item_key");
    }
}