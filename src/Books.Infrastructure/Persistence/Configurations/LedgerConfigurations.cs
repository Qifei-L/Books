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
