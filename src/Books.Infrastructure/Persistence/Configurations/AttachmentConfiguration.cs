using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.ToTable("attachments", t => t.HasCheckConstraint("ck_attachments_record_kind",
            "record_kind IN ('journal', 'sales_invoice', 'sales_credit_note', 'purchase_invoice', 'purchase_debit_note')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.RecordKind).HasMaxLength(40);
        b.Property(x => x.FileName).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.StorageKey).HasMaxLength(300);
        b.Property(x => x.Hash).HasMaxLength(64);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Ledger>().WithMany().HasForeignKey(x => x.LedgerId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.LedgerId, x.RecordKind, x.RecordId });
    }
}
