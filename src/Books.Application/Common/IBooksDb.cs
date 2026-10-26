using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Books.Domain.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Books.Application.Common;

public interface IBooksDb
{
    DbSet<User> Users { get; }
    DbSet<UserIdentity> Identities { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<LedgerGrant> LedgerGrants { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<InvitationGrant> InvitationGrants { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<Company> Companies { get; }
    DbSet<Ledger> Ledgers { get; }
    DbSet<Account> Accounts { get; }
    DbSet<AccountGroup> AccountGroups { get; }
    DbSet<AccountPurpose> AccountPurposes { get; }
    DbSet<AccountUsage> AccountUsages { get; }
    DbSet<Party> Parties { get; }
    DbSet<TaxCode> TaxCodes { get; }
    DbSet<BankAccount> BankAccounts { get; }
    DbSet<ExchangeRate> ExchangeRates { get; }
    DbSet<ItemCategory> ItemCategories { get; }
    DbSet<Item> Items { get; }
    DbSet<ItemUnit> ItemUnits { get; }
    DbSet<ItemComponent> ItemComponents { get; }
    DbSet<Assembly> Assemblies { get; }
    DbSet<AssemblyLine> AssemblyLines { get; }
    DbSet<AccountClass> Classes { get; }
    DbSet<OrganizationBranding> OrganizationBranding { get; }
    DbSet<LedgerReportSettings> LedgerReportSettings { get; }
    DbSet<ReportTemplate> ReportTemplates { get; }
    DbSet<ReportRun> ReportRuns { get; }
    DbSet<ReportShare> ReportShares { get; }
    DbSet<Location> Locations { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferLine> StockTransferLines { get; }
    DbSet<BankTransfer> BankTransfers { get; }
    DbSet<PurchaseInvoice> PurchaseInvoices { get; }
    DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines { get; }
    DbSet<DocumentTaxCurrency> DocumentTaxCurrencies { get; }
    DbSet<SalesInvoice> SalesInvoices { get; }
    DbSet<SalesInvoiceLine> SalesInvoiceLines { get; }
    DbSet<InvoiceSettings> InvoiceSettings { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentLine> PaymentLines { get; }
    DbSet<BankClearing> BankClearings { get; }
    DbSet<BankStatement> BankStatements { get; }
    DbSet<PayRun> PayRuns { get; }
    DbSet<PayRunLine> PayRunLines { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<FxRevaluation> FxRevaluations { get; }
    DbSet<FxRevaluationLine> FxRevaluationLines { get; }
    DbSet<StockAdjustmentLine> StockAdjustmentLines { get; }
    DbSet<StockAdjustmentEntry> StockAdjustmentEntries { get; }
    DbSet<FixedAsset> FixedAssets { get; }
    DbSet<DepreciationRun> DepreciationRuns { get; }
    DbSet<DepreciationLine> DepreciationLines { get; }
    DbSet<FiledTaxReturn> FiledTaxReturns { get; }
    DbSet<ReturnDefinitionVersion> ReturnDefinitionVersions { get; }
    DbSet<Journal> Journals { get; }
    DbSet<JournalRevision> JournalRevisions { get; }
    DbSet<JournalLine> JournalLines { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
