using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class OrganizationBrandingConfiguration : IEntityTypeConfiguration<OrganizationBranding>
{
    public void Configure(EntityTypeBuilder<OrganizationBranding> b)
    {
        b.ToTable("organization_branding");
        b.HasKey(x => x.OrgId);
        b.Property(x => x.LegalName).HasMaxLength(200);
        b.Property(x => x.LegalNameKm).HasMaxLength(200);
        b.Property(x => x.VatTin).HasMaxLength(50);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.BrandColor).HasMaxLength(20);
        b.Property(x => x.Footer).HasMaxLength(500);
        b.Property(x => x.PreparedBy).HasMaxLength(200);
        b.Property(x => x.ReviewedBy).HasMaxLength(200);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LedgerReportSettingsConfiguration : IEntityTypeConfiguration<LedgerReportSettings>
{
    public void Configure(EntityTypeBuilder<LedgerReportSettings> b)
    {
        b.ToTable("ledger_report_settings", t =>
        {
            t.HasCheckConstraint("ck_ledger_report_settings_language", "language IN ('en', 'km', 'bilingual')");
            t.HasCheckConstraint("ck_ledger_report_settings_orientation", "orientation IN ('portrait', 'landscape')");
            t.HasCheckConstraint("ck_ledger_report_settings_watermark", "watermark IN ('none', 'draft', 'final')");
        });
        b.HasKey(x => x.LedgerId);
        b.Property(x => x.Language).HasMaxLength(20);
        b.Property(x => x.Comparative).HasMaxLength(30);
        b.Property(x => x.PaperSize).HasMaxLength(10);
        b.Property(x => x.Orientation).HasMaxLength(20);
        b.Property(x => x.Watermark).HasMaxLength(20);
        b.Property(x => x.PackSections).HasMaxLength(100);
        b.HasOne<Ledger>().WithMany().HasForeignKey(x => x.LedgerId).OnDelete(DeleteBehavior.Cascade);
    }
}
