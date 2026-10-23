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

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> b)
    {
        b.ToTable("organizations", t => t.HasCheckConstraint("ck_organizations_state", "state IN ('active', 'locked_recovery')"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("memberships", t =>
        {
            t.HasCheckConstraint("ck_memberships_role", "role IN ('admin', 'member')");
            t.HasCheckConstraint("ck_memberships_status", "status IN ('active', 'removed')");
            t.HasCheckConstraint("ck_memberships_generation", "generation > 0");
        });
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.OrgId, x.Id });
        b.HasIndex(x => new { x.OrgId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.Status, x.OrgId });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("audit_events");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ActorClientId).HasMaxLength(200);
        b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.TargetType).HasMaxLength(50);
        b.Property(x => x.Changes).HasColumnType("jsonb");
        b.Property(x => x.RequestId).HasMaxLength(100);
        b.HasIndex(x => new { x.OrgId, x.OccurredAt, x.Id });
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_records");
        b.HasKey(x => new { x.ActorUserId, x.ClientId, x.ScopeKey, x.Operation, x.Key });
        b.Property(x => x.ClientId).HasMaxLength(200);
        b.Property(x => x.ScopeKey).HasMaxLength(50);
        b.Property(x => x.Operation).HasMaxLength(100);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.ResponseBody).HasColumnType("jsonb");
        b.HasIndex(x => x.ExpiresAt);
    }
}
