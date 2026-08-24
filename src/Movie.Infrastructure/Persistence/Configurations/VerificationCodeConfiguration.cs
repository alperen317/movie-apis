using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class VerificationCodeConfiguration : IEntityTypeConfiguration<VerificationCode>
{
    public void Configure(EntityTypeBuilder<VerificationCode> builder)
    {
        builder.ToTable("verification_codes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Purpose).HasLowerCaseStringConversion().HasMaxLength(20);

        builder.Property(x => x.CodeHash).HasMaxLength(255);

        builder.Property(x => x.Attempts).HasColumnType("smallint");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The lookup on every verify: this user's live code for this purpose.
        builder.HasIndex(x => new { x.UserId, x.Purpose, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("verification_codes_user_purpose_idx");
    }
}