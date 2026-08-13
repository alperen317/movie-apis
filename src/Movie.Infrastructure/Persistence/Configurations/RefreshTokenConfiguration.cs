using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Movie.Domain.Users;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).HasMaxLength(64);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Every refresh is a lookup by this value, and the uniqueness is what
        // guarantees one row can only ever describe one token.
        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("refresh_tokens_token_hash_key");

        // Used when a replayed token forces every session for that user to be
        // dropped at once.
        builder.HasIndex(x => new { x.UserId, x.RevokedAt })
            .HasDatabaseName("refresh_tokens_user_idx");
    }
}
