using Books.Domain.Access;
using Books.Domain.Ledgers;
using Books.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> b)
    {
        b.ToTable("report_runs", t =>
        {
            t.HasCheckConstraint("ck_report_runs_status", "status IN ('ready', 'failed')");
            t.HasCheckConstraint("ck_report_runs_file_kind", "file_kind IN ('pdf', 'xlsx')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Kind).HasMaxLength(40);
        b.Property(x => x.Period).HasMaxLength(40);
        b.Property(x => x.FileKind).HasMaxLength(10);
        b.Property(x => x.Status).HasMaxLength(20);
        b.Property(x => x.StorageKey).HasMaxLength(300);
        b.Property(x => x.Hash).HasMaxLength(64);
        b.Property(x => x.FileName).HasMaxLength(300);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.Snapshot).HasColumnType("jsonb").IsRequired();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Ledger>().WithMany().HasForeignKey(x => x.LedgerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ReportTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.LedgerId, x.GeneratedAt });
    }
}

internal sealed class ReportShareConfiguration : IEntityTypeConfiguration<ReportShare>
{
    public void Configure(EntityTypeBuilder<ReportShare> b)
    {
        b.ToTable("report_shares");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Token).HasMaxLength(100).IsRequired();
        b.HasOne<ReportRun>().WithMany().HasForeignKey(x => x.ReportRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => x.ReportRunId);
    }
}
