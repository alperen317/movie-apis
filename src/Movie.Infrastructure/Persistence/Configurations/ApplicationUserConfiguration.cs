using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("users");

        builder.Property(x => x.DisplayName).HasMaxLength(60);

        builder.Property(x => x.AvatarVariant)
            .HasLowerCaseStringConversion()
            .HasMaxLength(20)
            .HasDefaultValue(AvatarVariant.Beam);

        builder.Property(x => x.AvatarSeed).HasMaxLength(64);

        // ISO 3166-1 alpha-2.
        builder.Property(x => x.WatchRegion).HasMaxLength(2);

        // Identity names these "EmailIndex" and "UserNameIndex", which the
        // snake-case convention leaves alone — they would be the only quoted
        // mixed-case identifiers in the schema.
        builder.HasIndex(x => x.NormalizedEmail).HasDatabaseName("users_email_idx");
        builder.HasIndex(x => x.NormalizedUserName).HasDatabaseName("users_user_name_key");
    }
}
