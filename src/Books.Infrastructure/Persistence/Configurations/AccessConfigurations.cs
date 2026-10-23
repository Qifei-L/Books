using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", t => t.HasCheckConstraint("ck_users_status", "status IN ('active', 'disabled')"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.DisplayName).HasMaxLength(200);
    }
}

internal sealed class UserIdentityConfiguration : IEntityTypeConfiguration<UserIdentity>
{
    public void Configure(EntityTypeBuilder<UserIdentity> b)
    {
        b.ToTable("identities");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Issuer).HasMaxLength(500);
        b.Property(x => x.Subject).HasMaxLength(500);
        b.Property(x => x.VerifiedEmail).HasMaxLength(320);
        b.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
