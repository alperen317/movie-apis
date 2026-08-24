using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Library;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class RecommendationFeedbackConfiguration
    : IEntityTypeConfiguration<RecommendationFeedback>
{
    public void Configure(EntityTypeBuilder<RecommendationFeedback> builder)
    {
        builder.ToTable("recommendation_feedback");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MediaType).HasLowerCaseStringConversion().HasMaxLength(10);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Dismissing the same title twice is a no-op rather than an error.
        builder.HasIndex(x => new { x.UserId, x.MediaType, x.MediaId })
            .IsUnique()
            .HasDatabaseName("recommendation_feedback_user_media_key");
    }
}