using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("companies");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasAlternateKey(x => new { x.OrgId, x.Id });
        b.HasIndex(x => new { x.OrgId, x.CreatedAt, x.Id });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Restrict);
        // The reverse FK (companies → ledgers) is created in SQL by the migration; both directions are deferred.
    }
}

internal sealed class LedgerConfiguration : IEntityTypeConfiguration<Ledger>
{
    public void Configure(EntityTypeBuilder<Ledger> b)
    {
        b.ToTable("ledgers", t => t.HasCheckConstraint("ck_ledgers_fy_start_month", "fy_start_month BETWEEN 1 AND 12"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.BaseCurrency).HasMaxLength(3).IsFixedLength();
        b.HasAlternateKey(x => new { x.OrgId, x.Id });
        b.HasAlternateKey(x => new { x.OrgId, x.CompanyId });
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.HasOne<Company>().WithOne()
            .HasForeignKey<Ledger>(x => new { x.OrgId, x.CompanyId })
            .HasPrincipalKey<Company>(x => new { x.OrgId, x.Id })
            .HasConstraintName("fk_ledgers_companies")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LedgerGrantConfiguration : IEntityTypeConfiguration<LedgerGrant>
{
    public void Configure(EntityTypeBuilder<LedgerGrant> b)
    {
        b.ToTable("ledger_grants", t => t.HasCheckConstraint("ck_ledger_grants_access", "access IN ('read', 'edit')"));
        b.HasKey(x => new { x.MembershipId, x.LedgerId });
        b.HasIndex(x => new { x.OrgId, x.LedgerId });
        // Composite keys keep a grant's member and ledger inside the same organization.
        b.HasOne<Membership>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.MembershipId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("accounts", t => t.HasCheckConstraint(
            "ck_accounts_type", "type IN ('assets', 'liabilities', 'equity', 'income', 'cost_and_expense')"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(Account.MaxCodeLength);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        // Rule 16: Code is unique within a ledger; other ledgers may reuse it.
        b.HasIndex(x => new { x.LedgerId, x.Code }).IsUnique();
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // The composite key keeps a parent inside the same ledger.
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.ParentId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
