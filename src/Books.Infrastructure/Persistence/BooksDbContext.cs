using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Infrastructure.Persistence;

public sealed class BooksDbContext(DbContextOptions<BooksDbContext> options) : DbContext(options), IBooksDb
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> Identities => Set<UserIdentity>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<LedgerGrant> LedgerGrants => Set<LedgerGrant>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<InvitationGrant> InvitationGrants => Set<InvitationGrant>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountGroup> AccountGroups => Set<AccountGroup>();
    public DbSet<AccountPurpose> AccountPurposes => Set<AccountPurpose>();
    public DbSet<AccountUsage> AccountUsages => Set<AccountUsage>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<ItemCategory> ItemCategories => Set<ItemCategory>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<BankTransfer> BankTransfers => Set<BankTransfer>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();
    public DbSet<DocumentTaxCurrency> DocumentTaxCurrencies => Set<DocumentTaxCurrency>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<InvoiceSettings> InvoiceSettings => Set<InvoiceSettings>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentLine> PaymentLines => Set<PaymentLine>();
    public DbSet<BankClearing> BankClearings => Set<BankClearing>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<PayRun> PayRuns => Set<PayRun>();
    public DbSet<PayRunLine> PayRunLines => Set<PayRunLine>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();
    public DbSet<FixedAsset> FixedAssets => Set<FixedAsset>();
    public DbSet<DepreciationRun> DepreciationRuns => Set<DepreciationRun>();
    public DbSet<DepreciationLine> DepreciationLines => Set<DepreciationLine>();
    public DbSet<FiledTaxReturn> FiledTaxReturns => Set<FiledTaxReturn>();
    public DbSet<ReturnDefinitionVersion> ReturnDefinitionVersions => Set<ReturnDefinitionVersion>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<JournalRevision> JournalRevisions => Set<JournalRevision>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BooksDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as snake_case text guarded by CHECK constraints, so SQL stays readable.
        configurationBuilder.Properties<UserStatus>().HaveConversion<LowerCaseEnumConverter<UserStatus>>().HaveMaxLength(20);
        configurationBuilder.Properties<OrganizationState>().HaveConversion<LowerCaseEnumConverter<OrganizationState>>().HaveMaxLength(20);
        configurationBuilder.Properties<MembershipRole>().HaveConversion<LowerCaseEnumConverter<MembershipRole>>().HaveMaxLength(20);
        configurationBuilder.Properties<LedgerAccess>().HaveConversion<LowerCaseEnumConverter<LedgerAccess>>().HaveMaxLength(10);
        configurationBuilder.Properties<InvitationStatus>().HaveConversion<LowerCaseEnumConverter<InvitationStatus>>().HaveMaxLength(20);
        configurationBuilder.Properties<AccountType>().HaveConversion<LowerCaseEnumConverter<AccountType>>().HaveMaxLength(20);
        configurationBuilder.Properties<JournalKind>().HaveConversion<LowerCaseEnumConverter<JournalKind>>().HaveMaxLength(20);
        configurationBuilder.Properties<RevisionStatus>().HaveConversion<LowerCaseEnumConverter<RevisionStatus>>().HaveMaxLength(20);
        configurationBuilder.Properties<SystemPurpose>().HaveConversion<LowerCaseEnumConverter<SystemPurpose>>().HaveMaxLength(40);
        configurationBuilder.Properties<TaxKind>().HaveConversion<LowerCaseEnumConverter<TaxKind>>().HaveMaxLength(20);
        configurationBuilder.Properties<Books.Domain.Documents.DocumentStatus>().HaveConversion<LowerCaseEnumConverter<Books.Domain.Documents.DocumentStatus>>().HaveMaxLength(20);
        configurationBuilder.Properties<Books.Domain.Documents.DefinitionStatus>().HaveConversion<LowerCaseEnumConverter<Books.Domain.Documents.DefinitionStatus>>().HaveMaxLength(20);
        configurationBuilder.Properties<Books.Domain.Documents.PaymentLineKind>().HaveConversion<LowerCaseEnumConverter<Books.Domain.Documents.PaymentLineKind>>().HaveMaxLength(20);
        configurationBuilder.Properties<Books.Domain.Documents.StatementLineStatus>().HaveConversion<LowerCaseEnumConverter<Books.Domain.Documents.StatementLineStatus>>().HaveMaxLength(20);
        configurationBuilder.Properties<Usage>().HaveConversion<LowerCaseEnumConverter<Usage>>().HaveMaxLength(40);
        configurationBuilder.Properties<MembershipStatus>().HaveConversion<LowerCaseEnumConverter<MembershipStatus>>().HaveMaxLength(20);
    }
}
