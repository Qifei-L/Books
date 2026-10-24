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
        b.Ignore(x => x.SourceKind);
        // Invoices and debit notes are numbered in separate series.
        b.HasIndex(x => new { x.LedgerId, x.IsDebitNote, x.InvoiceNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.SupplierId });
        b.HasOne<PurchaseInvoice>().WithMany().HasForeignKey(x => x.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);
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

internal sealed class DocumentTaxCurrencyConfiguration : IEntityTypeConfiguration<DocumentTaxCurrency>
{
    public void Configure(EntityTypeBuilder<DocumentTaxCurrency> b)
    {
        b.ToTable("document_tax_currencies");
        b.HasKey(x => x.DocumentId);
        b.Property(x => x.DocumentKind).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Rate).HasPrecision(20, 10);
        b.Property(x => x.RateSource).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.GrossTotal).HasPrecision(20, 4);
        b.Property(x => x.TaxTotal).HasPrecision(20, 4);
        b.HasOne<Ledger>().WithMany().HasForeignKey(x => x.LedgerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> b)
    {
        b.ToTable("sales_invoices", t => t.HasCheckConstraint(
            "ck_sales_invoices_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL) AND due_date >= date"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.Ignore(x => x.NetTotal);
        b.Ignore(x => x.TaxTotal);
        b.Ignore(x => x.GrossTotal);
        b.Ignore(x => x.SourceKind);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        // Invoices and credit notes are numbered in separate series.
        b.HasIndex(x => new { x.LedgerId, x.IsCreditNote, x.InvoiceNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.CustomerId });
        b.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => x.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Party>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.CustomerId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.ReceivableAccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> b)
    {
        b.ToTable("sales_invoice_lines");
        b.HasKey(x => new { x.InvoiceId, x.LineNo });
        b.Ignore(x => x.TaxAdjusted);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.TaxReason).HasMaxLength(500);
        foreach (var amount in new[] { nameof(SalesInvoiceLine.NetAmount), nameof(SalesInvoiceLine.TaxCalculated), nameof(SalesInvoiceLine.TaxAmount),
                     nameof(SalesInvoiceLine.Quantity), nameof(SalesInvoiceLine.UnitPrice) })
        {
            b.Property(amount).HasPrecision(20, 4);
        }
        b.HasIndex(x => x.AccountId);
        b.HasIndex(x => x.TaxCodeId);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany().HasForeignKey(x => x.TaxCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FiledTaxReturnConfiguration : IEntityTypeConfiguration<FiledTaxReturn>
{
    public void Configure(EntityTypeBuilder<FiledTaxReturn> b)
    {
        b.ToTable("filed_tax_returns", t => t.HasCheckConstraint("ck_filed_tax_returns_period", "EXTRACT(DAY FROM period) = 1"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Snapshot).HasColumnType("text");
        b.HasIndex(x => new { x.LedgerId, x.Period }).IsUnique();
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.FiledBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ReturnDefinitionVersionConfiguration : IEntityTypeConfiguration<ReturnDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<ReturnDefinitionVersion> b)
    {
        b.ToTable("return_definition_versions", t =>
        {
            t.HasCheckConstraint("ck_return_definition_versions_status", "status IN ('draft', 'published') AND (status = 'draft' OR published_at IS NOT NULL)");
            t.HasCheckConstraint("ck_return_definition_versions_month", "EXTRACT(DAY FROM effective_from) = 1");
        });
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Definition).HasColumnType("text");
        b.HasIndex(x => new { x.LedgerId, x.VersionNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.EffectiveFrom }).IsUnique().HasFilter("status = 'published'");
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments", t => t.HasCheckConstraint(
            "ck_payments_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL)"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.Ignore(x => x.Total);
        b.Ignore(x => x.SourceKind);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        // Payments and receipts are numbered in separate series.
        b.HasIndex(x => new { x.LedgerId, x.IsReceipt, x.PaymentNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.PartyId });
        b.HasIndex(x => new { x.LedgerId, x.BankAccountId, x.Date });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Party>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.PartyId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.BankAccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PaymentLineConfiguration : IEntityTypeConfiguration<PaymentLine>
{
    public void Configure(EntityTypeBuilder<PaymentLine> b)
    {
        b.ToTable("payment_lines", t => t.HasCheckConstraint("ck_payment_lines_kind",
            "(kind = 'invoice' AND invoice_id IS NOT NULL AND control_account_id IS NOT NULL AND account_id IS NULL) OR "
            + "(kind = 'party' AND invoice_id IS NULL AND control_account_id IS NOT NULL AND account_id IS NULL) OR "
            + "(kind = 'account' AND invoice_id IS NULL AND control_account_id IS NULL AND account_id IS NOT NULL)"));
        b.HasKey(x => new { x.PaymentId, x.LineNo });
        b.Ignore(x => x.TaxAdjusted);
        b.Ignore(x => x.Total);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.TaxReason).HasMaxLength(500);
        foreach (var amount in new[] { nameof(PaymentLine.NetAmount), nameof(PaymentLine.TaxCalculated), nameof(PaymentLine.TaxAmount),
                     nameof(PaymentLine.WithholdingAmount), nameof(PaymentLine.Quantity), nameof(PaymentLine.UnitPrice) })
        {
            b.Property(amount).HasPrecision(20, 4);
        }
        // The settled invoice is a purchase invoice (payments) or a sales invoice (receipts); checked by the service.
        b.HasIndex(x => x.InvoiceId);
        b.HasIndex(x => x.AccountId);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.ControlAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany().HasForeignKey(x => x.TaxCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany().HasForeignKey(x => x.WithholdingCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankClearingConfiguration : IEntityTypeConfiguration<BankClearing>
{
    public void Configure(EntityTypeBuilder<BankClearing> b)
    {
        b.ToTable("bank_clearings");
        b.HasKey(x => new { x.AccountId, x.JournalId });
        b.HasIndex(x => x.JournalId);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.AccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ClearedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
