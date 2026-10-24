using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Documents;
using Books.Application.Journals;
using Books.Application.Ledgers;
using Books.Application.Reporting;
using Books.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Books.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBooksInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BooksDbContext>(options => Configure(options, connectionString));
        services.AddScoped<IBooksDb>(sp => sp.GetRequiredService<BooksDbContext>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IdentityService>();
        services.AddScoped<WriteRunner>();
        services.AddScoped<ReadRunner>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<MembershipService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<AccountService>();
        services.AddScoped<AccountGroupService>();
        services.AddScoped<JournalService>();
        services.AddScoped<LedgerSettingsService>();
        services.AddScoped<ReportService>();
        services.AddScoped<StatementService>();
        services.AddScoped<PayablesReportService>();
        services.AddScoped<AccountPurposeService>();
        services.AddScoped<PartyService>();
        services.AddScoped<AccountUsageService>();
        services.AddScoped<TaxCodeService>();
        services.AddScoped<CambodiaTemplate>();
        services.AddScoped<BankService>();
        services.AddScoped<ItemService>();
        services.AddScoped<DocumentPosting>();
        services.AddScoped<BankTransferService>();
        services.AddScoped<PurchaseInvoiceService>();
        services.AddScoped<TaxCurrencyService>();
        services.AddScoped<SalesInvoiceService>();
        services.AddScoped<ReceivablesReportService>();
        services.AddScoped<TaxReturnService>();
        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(BooksDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();
}
