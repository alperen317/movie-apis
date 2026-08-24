using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Movie.Domain.Lists;

namespace Movie.Infrastructure.Persistence.Configurations;

public sealed class ListMemberConfiguration : IEntityTypeConfiguration<ListMember>
{
    public void Configure(EntityTypeBuilder<ListMember> builder)
    {
        builder.ToTable("list_members");

        builder.HasKey(x => x.Id);

        // ValueGeneratedNever, or EF leaves the column out of the INSERT
        // whenever the property holds its CLR default, and the database default
        // wins instead. MemberRole.Owner *is* that CLR default — it comes first
        // in the enum — so a creator's own membership was being written as a
        // plain member. The column defaults stay for anything that reaches the
        // table outside EF; what changes is that the value the model holds is
        // always the value that gets written.
        builder.Property(x => x.Role)
            .HasLowerCaseStringConversion()
            .HasMaxLength(10)
            .HasDefaultValue(MemberRole.Member)
            .ValueGeneratedNever();

        // Pending happens to agree with the CLR default, so this one was never
        // wrong. Spelled out the same way regardless: the two lining up is a
        // coincidence of the enum's order, not something to have to check.
        builder.Property(x => x.Status)
            .HasLowerCaseStringConversion()
            .HasMaxLength(10)
            .HasDefaultValue(MemberStatus.Pending)
            .ValueGeneratedNever();

        builder.Property(x => x.InvitedById).HasColumnName("invited_by");

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The inviter leaving must not erase the membership of everyone they
        // invited, so this one nulls out rather than cascading.
        builder.HasOne(x => x.InvitedBy)
            .WithMany()
            .HasForeignKey(x => x.InvitedById)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.ListId, x.UserId })
            .IsUnique()
            .HasDatabaseName("list_members_list_user_key");

        builder.HasIndex(x => new { x.ListId, x.Status }).HasDatabaseName("list_members_list_idx");

        builder.HasIndex(x => new { x.UserId, x.Status }).HasDatabaseName("list_members_user_idx");
    }
}