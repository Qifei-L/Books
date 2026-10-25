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
        b.Property(x => x.ToAmount).HasPrecision(20, 4);
        b.Property(x => x.FromRate).HasPrecision(20, 10);
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
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.ExchangeRate).HasPrecision(20, 10);
        b.Property(x => x.BaseTotal).HasPrecision(20, 4);
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
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.UnitFactor).HasPrecision(20, 4).HasDefaultValue(1m);
        b.Ignore(x => x.BaseQuantity);
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

internal sealed class InvoiceSettingsConfiguration : IEntityTypeConfiguration<InvoiceSettings>
{
    public void Configure(EntityTypeBuilder<InvoiceSettings> b)
    {
        b.ToTable("invoice_settings");
        b.HasKey(x => x.LedgerId);
        b.Property(x => x.NameKhmer).HasMaxLength(200);
        b.Property(x => x.NameEnglish).HasMaxLength(200);
        b.Property(x => x.TaxId).HasMaxLength(50);
        b.Property(x => x.AddressKhmer).HasMaxLength(500);
        b.Property(x => x.AddressEnglish).HasMaxLength(500);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.InvoicePrefix).HasMaxLength(20);
        b.Property(x => x.CreditNotePrefix).HasMaxLength(20);
        b.HasOne<Ledger>().WithMany().HasForeignKey(x => x.LedgerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> b)
    {
        b.ToTable("sales_invoices", t => t.HasCheckConstraint(
            "ck_sales_invoices_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL) AND due_date >= date"));
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.ExchangeRate).HasPrecision(20, 10);
        b.Property(x => x.BaseTotal).HasPrecision(20, 4);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.Ignore(x => x.NetTotal);
        b.Ignore(x => x.TaxTotal);
        b.Ignore(x => x.GrossTotal);
        b.Ignore(x => x.OtherTaxTotal);
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
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.UnitFactor).HasPrecision(20, 4).HasDefaultValue(1m);
        b.Ignore(x => x.BaseQuantity);
        b.ToTable("sales_invoice_lines");
        b.HasKey(x => new { x.InvoiceId, x.LineNo });
        b.Ignore(x => x.TaxAdjusted);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.TaxReason).HasMaxLength(500);
        foreach (var amount in new[] { nameof(SalesInvoiceLine.NetAmount), nameof(SalesInvoiceLine.TaxCalculated), nameof(SalesInvoiceLine.TaxAmount),
                     nameof(SalesInvoiceLine.Quantity), nameof(SalesInvoiceLine.UnitPrice), nameof(SalesInvoiceLine.OtherTaxAmount), nameof(SalesInvoiceLine.CostAmount) })
        {
            b.Property(amount).HasPrecision(20, 4);
        }
        b.HasIndex(x => x.AccountId);
        b.HasIndex(x => x.TaxCodeId);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany().HasForeignKey(x => x.TaxCodeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany().HasForeignKey(x => x.OtherTaxCodeId).OnDelete(DeleteBehavior.Restrict);
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
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(20, 10);
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
                     nameof(PaymentLine.WithholdingAmount), nameof(PaymentLine.Quantity), nameof(PaymentLine.UnitPrice),
                     nameof(PaymentLine.ControlAmount), nameof(PaymentLine.SettledAmount) })
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

internal sealed class BankStatementConfiguration : IEntityTypeConfiguration<BankStatement>
{
    public void Configure(EntityTypeBuilder<BankStatement> b)
    {
        b.ToTable("bank_statements");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.OpeningBalance).HasPrecision(20, 4);
        b.Property(x => x.ClosingBalance).HasPrecision(20, 4);
        b.HasIndex(x => new { x.LedgerId, x.AccountId, x.ToDate });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StatementId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.AccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ImportedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> b)
    {
        b.ToTable("bank_statement_lines", t => t.HasCheckConstraint("ck_bank_statement_lines_status", "status IN ('matched', 'drafted', 'duplicate')"));
        b.HasKey(x => new { x.StatementId, x.LineNo });
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.ExternalId).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(20, 4);
        b.HasIndex(x => x.ExternalId);
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayRunConfiguration : IEntityTypeConfiguration<PayRun>
{
    public void Configure(EntityTypeBuilder<PayRun> b)
    {
        b.ToTable("pay_runs", t => t.HasCheckConstraint(
            "ck_pay_runs_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL) AND EXTRACT(DAY FROM period) = 1"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.ComplexProperty(x => x.Accounts, a =>
        {
            a.Property(x => x.SalaryExpense).HasColumnName("salary_expense_account_id");
            a.Property(x => x.NssfExpense).HasColumnName("nssf_expense_account_id");
            a.Property(x => x.SalariesPayable).HasColumnName("salaries_payable_account_id");
            a.Property(x => x.SalaryTaxPayable).HasColumnName("salary_tax_payable_account_id");
            a.Property(x => x.NssfPayable).HasColumnName("nssf_payable_account_id");
            a.Property(x => x.OtherDeductions).HasColumnName("other_deductions_account_id");
        });
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Rate).HasPrecision(20, 10);
        b.HasIndex(x => new { x.LedgerId, x.PayRunNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.Period });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PayRunId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayRunLineConfiguration : IEntityTypeConfiguration<PayRunLine>
{
    public void Configure(EntityTypeBuilder<PayRunLine> b)
    {
        b.ToTable("pay_run_lines");
        b.HasKey(x => new { x.PayRunId, x.LineNo });
        b.Property(x => x.EmployeeCode).HasMaxLength(200);
        b.Property(x => x.Name).HasMaxLength(200);
        foreach (var amount in new[] { nameof(PayRunLine.Gross), nameof(PayRunLine.FringeBenefits), nameof(PayRunLine.SalaryTax), nameof(PayRunLine.FringeBenefitTax),
                     nameof(PayRunLine.NssfEmployee), nameof(PayRunLine.NssfEmployer), nameof(PayRunLine.OtherDeductions), nameof(PayRunLine.Net) })
        {
            b.Property(amount).HasPrecision(20, 4);
        }
    }
}

internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("stock_adjustments", t => t.HasCheckConstraint(
            "ck_stock_adjustments_status", "status IN ('draft', 'posted', 'deleted') AND (status <> 'posted' OR journal_id IS NOT NULL)"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.HasIndex(x => new { x.LedgerId, x.AdjustmentNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.Date });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.AdjustmentId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.AdjustmentId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Entries).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> b)
    {
        b.ToTable("stock_adjustment_lines", t => t.HasCheckConstraint("ck_stock_adjustment_lines_quantity", "quantity <> 0 OR value <> 0"));
        b.HasKey(x => new { x.AdjustmentId, x.LineNo });
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Quantity).HasPrecision(20, 4);
        b.Property(x => x.CountedQuantity).HasPrecision(20, 4);
        b.Property(x => x.BookQuantity).HasPrecision(20, 4);
        b.Property(x => x.UnitCost).HasPrecision(20, 4);
        b.Property(x => x.Value).HasPrecision(20, 4);
        b.HasIndex(x => x.ItemId);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FxRevaluationConfiguration : IEntityTypeConfiguration<FxRevaluation>
{
    public void Configure(EntityTypeBuilder<FxRevaluation> b)
    {
        b.ToTable("fx_revaluations", t => t.HasCheckConstraint(
            "ck_fx_revaluations_status", "status IN ('posted', 'deleted') AND journal_id IS NOT NULL AND reversal_journal_id IS NOT NULL"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Rates).HasColumnType("jsonb");
        b.HasIndex(x => new { x.LedgerId, x.RevaluationNo }).IsUnique();
        b.HasIndex(x => new { x.LedgerId, x.Date });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RevaluationId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.ReversalJournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FxRevaluationLineConfiguration : IEntityTypeConfiguration<FxRevaluationLine>
{
    public void Configure(EntityTypeBuilder<FxRevaluationLine> b)
    {
        b.ToTable("fx_revaluation_lines");
        b.HasKey(x => new { x.RevaluationId, x.LineNo });
        b.Ignore(x => x.Adjustment);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.ForeignAmount).HasPrecision(20, 4);
        b.Property(x => x.Before).HasPrecision(20, 4);
        b.Property(x => x.After).HasPrecision(20, 4);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockAdjustmentEntryConfiguration : IEntityTypeConfiguration<StockAdjustmentEntry>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentEntry> b)
    {
        b.ToTable("stock_adjustment_entries", t => t.HasCheckConstraint("ck_stock_adjustment_entries_amount", "amount <> 0"));
        b.HasKey(x => new { x.AdjustmentId, x.LineNo });
        b.Property(x => x.Amount).HasPrecision(20, 4);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FixedAssetConfiguration : IEntityTypeConfiguration<FixedAsset>
{
    public void Configure(EntityTypeBuilder<FixedAsset> b)
    {
        b.ToTable("fixed_assets");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(200);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Cost).HasPrecision(20, 4);
        b.Property(x => x.ResidualValue).HasPrecision(20, 4);
        b.Ignore(x => x.Monthly);
        b.HasIndex(x => new { x.LedgerId, x.Code }).IsUnique();
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AssetAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.AccumulatedDepreciationAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.DepreciationExpenseAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DepreciationRunConfiguration : IEntityTypeConfiguration<DepreciationRun>
{
    public void Configure(EntityTypeBuilder<DepreciationRun> b)
    {
        b.ToTable("depreciation_runs", t => t.HasCheckConstraint("ck_depreciation_runs_status", "status IN ('draft', 'posted', 'deleted') AND EXTRACT(DAY FROM period) = 1"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.LedgerId, x.Period }).IsUnique().HasFilter("status = 'posted'");
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DepreciationLineConfiguration : IEntityTypeConfiguration<DepreciationLine>
{
    public void Configure(EntityTypeBuilder<DepreciationLine> b)
    {
        b.ToTable("depreciation_lines");
        b.HasKey(x => new { x.RunId, x.LineNo });
        b.Property(x => x.Amount).HasPrecision(20, 4);
        b.HasOne<FixedAsset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
    }
}
