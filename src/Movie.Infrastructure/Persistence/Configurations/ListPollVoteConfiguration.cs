using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Lists;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class ListPollVoteConfiguration : IEntityTypeConfiguration<ListPollVote>
{
    public void Configure(EntityTypeBuilder<ListPollVote> builder)
    {
        builder.ToTable("list_poll_votes");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.Poll)
            .WithMany()
            .HasForeignKey(x => x.PollId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The rule that makes this an election rather than a pile of upvotes.
        // Changing your mind updates this row instead of adding another.
        builder.HasIndex(x => new { x.PollId, x.UserId })
            .IsUnique()
            .HasDatabaseName("list_poll_votes_poll_user_key");
    }
}