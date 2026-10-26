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
        // text, not jsonb: a replay returns the original response byte for byte.
        b.Property(x => x.ResponseBody).HasColumnType("text");
        b.HasIndex(x => x.ExpiresAt);
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("invitations", t =>
        {
            t.HasCheckConstraint("ck_invitations_status", "status IN ('pending', 'accepted', 'revoked', 'expired', 'invalidated')");
            t.HasCheckConstraint("ck_invitations_role", "proposed_role IN ('admin', 'member')");
        });
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TargetEmailKey).HasMaxLength(320);
        b.HasAlternateKey(x => new { x.OrgId, x.Id });
        // At most one pending invitation per organization and target; elapsed ones are marked expired first.
        b.HasIndex(x => new { x.OrgId, x.TargetEmailKey }).IsUnique().HasFilter("status = 'pending'");
        b.HasIndex(x => new { x.TargetEmailKey, x.Status, x.ExpiresAt });
        b.HasIndex(x => new { x.OrgId, x.InviterMembershipId, x.Status });
        b.HasOne<Membership>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.InviterMembershipId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AcceptedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InvitationGrantConfiguration : IEntityTypeConfiguration<InvitationGrant>
{
    public void Configure(EntityTypeBuilder<InvitationGrant> b)
    {
        b.ToTable("invitation_grants", t =>
        {
            t.HasCheckConstraint("ck_invitation_grants_access", "access IN ('read', 'edit')");
            t.HasCheckConstraint("ck_invitation_grants_role", "role IS NULL OR role IN ('viewer', 'bookkeeper', 'accountant', 'manager')");
        });
        b.Property(x => x.Role).HasMaxLength(20);
        b.HasKey(x => new { x.InvitationId, x.LedgerId });
        b.HasOne<Invitation>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.InvitationId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Books.Domain.Ledgers.Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
