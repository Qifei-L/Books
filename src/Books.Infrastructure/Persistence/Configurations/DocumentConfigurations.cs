using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class BankTransferConfiguration : IEntityTypeConfiguration<BankTransfer>
{
    public void Configure(EntityTypeBuilder<BankTransfer> b)
    {
        b.ToTable("bank_transfers", t =>
        {
            t.HasCheckConstraint("ck_bank_transfers_amounts", "amount > 0 AND fee_amount >= 0 AND from_account_id <> to_account_id");
            t.HasCheckConstraint("ck_bank_transfers_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL)");
        });
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Amount).HasPrecision(20, 4);
        b.Property(x => x.FeeAmount).HasPrecision(20, 4);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.HasIndex(x => new { x.LedgerId, x.TransferNo }).IsUnique();
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.FromAccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.ToAccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.FeeAccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> b)
    {
        b.ToTable("purchase_invoices", t => t.HasCheckConstraint(
            "ck_purchase_invoices_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL) AND due_date >= date"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.Ignore(x => x.NetTotal);
        b.Ignore(x => x.TaxTotal);
        b.Ignore(x => x.WithholdingTotal);
        b.Ignore(x => x.GrossTotal);
        b.Ignore(x => x.PayableTotal);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasIndex(x => new { x.LedgerId, x.InvoiceNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.SupplierId });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Party>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.SupplierId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.PayableAccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PurchaseInvoiceLineConfiguration : IEntityTypeConfiguration<PurchaseInvoiceLine>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoiceLine> b)
    {
        b.ToTable("purchase_invoice_lines");
        b.HasKey(x => new { x.InvoiceId, x.LineNo });
        b.Ignore(x => x.TaxAdjusted);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.TaxReason).HasMaxLength(500);
        foreach (var amount in new[] { nameof(PurchaseInvoiceLine.NetAmount), nameof(PurchaseInvoiceLine.TaxCalculated), nameof(PurchaseInvoiceLine.TaxAmount),
                     nameof(PurchaseInvoiceLine.WithholdingAmount), nameof(PurchaseInvoiceLine.Quantity), nameof(PurchaseInvoiceLine.UnitPrice) })
        {
            b.Property(amount).HasPrecision(20, 4);
        }
        b.HasIndex(x => x.AccountId);
        b.HasIndex(x => x.TaxCodeId);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany().HasForeignKey(x => x.TaxCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany().HasForeignKey(x => x.WithholdingCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
