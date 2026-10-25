using Books.Domain.Access;
using Books.Domain.Ledgers;
using Books.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class ReportTemplateConfiguration : IEntityTypeConfiguration<ReportTemplate>
{
    public void Configure(EntityTypeBuilder<ReportTemplate> b)
    {
        b.ToTable("report_templates", t =>
        {
            t.HasCheckConstraint("ck_report_templates_status", "status IN ('draft', 'published')");
            t.HasCheckConstraint("ck_report_templates_kind",
                "kind IN ('client_pack', 'financial_statements', 'trial_balance', 'gl_detail', 'aging', 'tax_return', 'tax_workpaper')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Definition).HasColumnType("jsonb").IsRequired();
        b.Property(x => x.UpdatedAt);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Ledger>().WithMany().HasForeignKey(x => x.LedgerId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.OrgId, x.LedgerId, x.Kind, x.Status });
        b.HasIndex(x => new { x.OrgId, x.LedgerId, x.Kind, x.Version }).IsUnique();
    }
}
