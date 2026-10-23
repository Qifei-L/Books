using System.Text.Json;
using Books.Api.Auth;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Application.Ledgers;
using Books.Domain.Journals;
using Books.Domain.Ledgers;

namespace Books.Api.Dev;

/// <summary>
/// Development only: fills a new organization with a small, realistic ledger so the read-only UI has something
/// to show. It goes through the normal application services, so every rule and audit applies.
/// </summary>
public static class DemoData
{
    public static void MapDemoDataEndpoint(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }
        app.MapPost("/dev/demo", async (PrincipalAccessor principals, IServiceProvider services, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var orgId = await SeedAsync(principal, services, ct);
            return Results.Ok(new { data = new { organization_id = orgId } });
        }).RequireAuthorization(AuthSetup.ApiPolicy);
    }

    private static async Task<Guid> SeedAsync(Principal principal, IServiceProvider services, CancellationToken ct)
    {
        var organizations = services.GetRequiredService<OrganizationService>();
        var companies = services.GetRequiredService<CompanyService>();
        var groups = services.GetRequiredService<AccountGroupService>();
        var accounts = services.GetRequiredService<AccountService>();
        var purposes = services.GetRequiredService<AccountPurposeService>();
        var journals = services.GetRequiredService<JournalService>();
        var settings = services.GetRequiredService<LedgerSettingsService>();

        var org = Data(await organizations.CreateAsync(principal, Meta(), $"Demo Trading {DateTime.UtcNow:HHmmss}", ct));
        var orgId = org.GetProperty("organization").GetProperty("id").GetGuid();
        var ledgerId = Data(await companies.CreateAsync(principal, Meta(), orgId, new CreateCompany("Demo Trading Co., Ltd.", "USD", 1), ct))
            .GetProperty("ledger").GetProperty("id").GetGuid();

        async Task<Guid> Group(string name, AccountType type) =>
            Data(await groups.CreateAsync(principal, Meta(), orgId, ledgerId, name, type, null, ct)).GetProperty("id").GetGuid();
        var current = await Group("Current assets", AccountType.Assets);
        var liabilities = await Group("Current liabilities", AccountType.Liabilities);
        var equity = await Group("Equity", AccountType.Equity);
        var revenue = await Group("Revenue", AccountType.Income);
        var expenses = await Group("Operating expenses", AccountType.CostAndExpense);

        async Task<Guid> Account(string code, string name, AccountType type, Guid group, bool bank = false) =>
            Data(await accounts.CreateAsync(principal, Meta(), orgId, ledgerId, new CreateAccount(code, name, type, group, false, bank, null), ct))
                .GetProperty("id").GetGuid();
        var cash = await Account("1000", "Cash on hand", AccountType.Assets, current);
        var bank = await Account("1100", "ABA Bank USD", AccountType.Assets, current, bank: true);
        var vat = await Account("2000", "VAT payable", AccountType.Liabilities, liabilities);
        var suspense = await Account("2999", "Suspense", AccountType.Liabilities, liabilities);
        var capital = await Account("3000", "Owner capital", AccountType.Equity, equity);
        var retained = await Account("3100", "Retained earnings", AccountType.Equity, equity);
        var clearing = await Account("3900", "Opening balance clearing", AccountType.Equity, equity);
        var sales = await Account("4000", "Sales", AccountType.Income, revenue);
        var service = await Account("4100", "Service income", AccountType.Income, revenue);
        var rent = await Account("5000", "Rent", AccountType.CostAndExpense, expenses);
        var salaries = await Account("5100", "Salaries", AccountType.CostAndExpense, expenses);
        var utilities = await Account("5200", "Utilities", AccountType.CostAndExpense, expenses);

        long ledgerVersion = 1;
        foreach (var (purpose, account) in new[] { (SystemPurpose.RetainedEarnings, retained), (SystemPurpose.OpeningBalanceClearing, clearing), (SystemPurpose.Suspense, suspense) })
        {
            await purposes.AssignAsync(principal, Meta(ledgerVersion++), orgId, ledgerId, purpose, account, ct);
        }

        async Task Post(string date, string reference, string description, params (Guid Account, decimal Amount)[] lines) =>
            await journals.CreateAsync(principal, Meta(), orgId, ledgerId,
                new JournalDraft(DateOnly.Parse(date), lines.Select(l => new DraftLine(l.Account, l.Amount)).ToList(), reference, description), true, ct);

        await Post("2025-11-20", "INV-2025-118", "Prior-year sales", (bank, 4_800m), (sales, -4_800m));
        await Post("2026-01-01", "OB-1", "Opening bank balance", (bank, 20_000m), (clearing, -20_000m));
        await Post("2026-01-01", "OB-2", "Owner capital at opening", (clearing, 20_000m), (capital, -20_000m));
        var monthlySales = new[] { 4_200m, 3_900m, 5_150m, 4_780m, 5_600m, 6_020m, 5_430m, 4_990m, 5_870m, 6_310m };
        for (var month = 1; month <= 10; month++)
        {
            var day = $"2026-{month:00}";
            var net = monthlySales[month - 1];
            var tax = Math.Round(net * 0.10m, 2);
            await Post($"{day}-05", $"INV-26{month:00}-01", "Monthly sales", (bank, net + tax), (sales, -net), (vat, -tax));
            await Post($"{day}-10", $"RENT-{month:00}", "Office rent", (rent, 1_200m), (bank, -1_200m));
            await Post($"{day}-25", $"PAY-{month:00}", "Salaries", (salaries, 2_500m), (bank, -2_500m));
            await Post($"{day}-28", $"EDC-{month:00}", "Electricity", (utilities, 140m + month * 3), (cash, -(140m + month * 3)));
            if (month % 3 == 0)
            {
                await Post($"{day}-15", $"SVC-{month:00}", "Consulting service", (cash, 900m), (service, -900m));
            }
        }
        await Post("2026-10-18", "BANK-UNK", "Unidentified bank receipt", (bank, 75m), (suspense, -75m));
        await journals.CreateAsync(principal, Meta(), orgId, ledgerId,
            new JournalDraft(DateOnly.Parse("2026-10-30"), [new DraftLine(rent, 1_200m), new DraftLine(bank, -1_200m)], "RENT-11", "November rent (draft)"), false, ct);
        await settings.CloseAsync(principal, Meta(ledgerVersion), orgId, ledgerId, DateOnly.Parse("2026-09-30"), ct);
        return orgId;
    }

    private static WriteMeta Meta(long? version = null) => new(Guid.NewGuid(), Guid.NewGuid().ToString("N"), version, "dev-demo");

    private static JsonElement Data(WriteResult result) =>
        result.Status < 300 ? JsonDocument.Parse(result.Json).RootElement.Clone() : throw new InvalidOperationException($"Demo seed failed: {result.Json}");
}
