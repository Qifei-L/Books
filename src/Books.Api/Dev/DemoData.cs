using System.Text.Json;
using Books.Api.Auth;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Documents;
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
        var partyService = services.GetRequiredService<PartyService>();
        var taxCodes = services.GetRequiredService<TaxCodeService>();
        var banks = services.GetRequiredService<BankService>();
        var items = services.GetRequiredService<ItemService>();
        var transfers = services.GetRequiredService<BankTransferService>();

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
        var acleda = await Account("1200", "ACLEDA Bank USD", AccountType.Assets, current, bank: true);
        var vatInput = await Account("1300", "VAT receivable (input)", AccountType.Assets, current);
        var wht = await Account("2100", "Withholding tax payable", AccountType.Liabilities, liabilities);
        var bankCharges = await Account("5300", "Bank charges", AccountType.CostAndExpense, expenses);

        // Ledger setup added in M4: bank details, Cambodian tax codes, parties, items and monthly NBC rates.
        await banks.CreateAsync(principal, Meta(), orgId, ledgerId, bank, "ABA Bank", "000 123 456", ct);
        await banks.CreateAsync(principal, Meta(), orgId, ledgerId, acleda, "ACLEDA Bank", "0001-01-234567-1-1", ct);
        async Task<Guid> Tax(string name, TaxKind kind, decimal rate, Guid? account, string? from = null) =>
            Data(await taxCodes.CreateAsync(principal, Meta(), orgId, ledgerId,
                new TaxCodeInput(name, kind, rate, account, from is null ? null : DateOnly.Parse(from), null), ct)).GetProperty("id").GetGuid();
        var vat10 = await Tax("VAT 10% (sales)", TaxKind.VatOutput, 10m, vat);
        await Tax("VAT 10% (purchases)", TaxKind.VatInput, 10m, vatInput);
        await Tax("VAT 0% (zero-rated)", TaxKind.VatZeroRated, 0m, null);
        await Tax("Non-VAT", TaxKind.NonVat, 0m, null);
        var fuel4 = await Tax("VAT 4% fuel (sales)", TaxKind.VatOutput, 4m, vat, "2026-03-01");
        await Tax("WHT 15% services, interest, royalties", TaxKind.Withholding, 15m, wht);
        await Tax("WHT 10% rent", TaxKind.Withholding, 10m, wht);
        await Tax("WHT 14% non-resident", TaxKind.Withholding, 14m, wht);
        async Task<Guid> Party(string name, bool customer, string? taxId, int? terms, string? contact) =>
            Data(await partyService.CreateAsync(principal, Meta(), orgId, ledgerId,
                new PartyInput(name, taxId, customer, !customer, null, null, "Phnom Penh", contact, terms, true), ct)).GetProperty("id").GetGuid();
        var customers = new[]
        {
            await Party("Mekong Retail Co., Ltd.", true, "K001-901234567", 30, "Dara"),
            await Party("Angkor Hotels Plc", true, "K002-902345678", 15, "Sophea"),
            await Party("Riverside Café", true, null, 0, null),
        };
        await Party("Phnom Penh Property Ltd.", false, "K003-903456789", 0, "Vuthy");
        await Party("Electricité du Cambodge", false, "E000-100000001", 7, null);
        var consulting = Data(await items.CreateCategoryAsync(principal, Meta(), orgId, ledgerId, "Consulting", ct)).GetProperty("id").GetGuid();
        var goods = Data(await items.CreateCategoryAsync(principal, Meta(), orgId, ledgerId, "Goods", ct)).GetProperty("id").GetGuid();
        await items.CreateAsync(principal, Meta(), orgId, ledgerId, new ItemInput("Advisory hour", "Senior consultant time", "hour", consulting, service, vat10), ct);
        await items.CreateAsync(principal, Meta(), orgId, ledgerId, new ItemInput("Trading goods", null, "pcs", goods, sales, vat10), ct);
        await items.CreateAsync(principal, Meta(), orgId, ledgerId, new ItemInput("Diesel", "Resale fuel", "litre", goods, sales, fuel4), ct);
        for (var month = 1; month <= 10; month++)
        {
            var monthEnd = new DateOnly(2026, month, 1).AddMonths(1).AddDays(-1);
            await banks.SetRateAsync(principal, Meta(), orgId, monthEnd, "USD", "KHR", 4_050m + month * 7, "NBC", ct);
        }

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
            await journals.CreateAsync(principal, Meta(), orgId, ledgerId, new JournalDraft(DateOnly.Parse($"{day}-05"),
                [new DraftLine(bank, net + tax), new DraftLine(sales, -net, PartyId: customers[month % 3]), new DraftLine(vat, -tax)],
                $"INV-26{month:00}-01", "Monthly sales"), true, ct);
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
        await transfers.CreateAsync(principal, Meta(), orgId, ledgerId,
            new BankTransferInput(DateOnly.Parse("2026-10-20"), bank, acleda, 3_000m, 2m, bankCharges, "TT-2026-014", "Move funds to ACLEDA"), true, ct);
        await settings.CloseAsync(principal, Meta(ledgerVersion), orgId, ledgerId, DateOnly.Parse("2026-09-30"), ct);
        return orgId;
    }

    private static WriteMeta Meta(long? version = null) => new(Guid.NewGuid(), Guid.NewGuid().ToString("N"), version, "dev-demo");

    private static JsonElement Data(WriteResult result) =>
        result.Status < 300 ? JsonDocument.Parse(result.Json).RootElement.Clone() : throw new InvalidOperationException($"Demo seed failed: {result.Json}");
}
