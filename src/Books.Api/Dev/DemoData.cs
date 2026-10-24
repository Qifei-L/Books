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

        // Tax accounts, AP/AR, system purposes and the tax codes come from the Cambodia starter template.
        await services.GetRequiredService<CambodiaTemplate>().ApplyAsync(principal, Meta(), orgId, ledgerId, ct);
        var chart = (await accounts.ListAsync(principal, orgId, ledgerId, ct)).ToDictionary(a => a.Code, a => a.Id);
        var taxes = (await taxCodes.ListAsync(principal, orgId, ledgerId, ct)).ToDictionary(t => t.Name, t => t.Id);
        var groupIds = (await groups.ListAsync(principal, orgId, ledgerId, ct)).ToDictionary(g => g.Name, g => g.Id);
        var current = groupIds["Current assets"];
        var equity = groupIds["Equity"];
        var expenses = groupIds["Operating expenses"];
        var revenue = Data(await groups.CreateAsync(principal, Meta(), orgId, ledgerId, "Revenue", AccountType.Income, null, ct)).GetProperty("id").GetGuid();

        async Task<Guid> Account(string code, string name, AccountType type, Guid group) =>
            Data(await accounts.CreateAsync(principal, Meta(), orgId, ledgerId, new CreateAccount(code, name, type, group, false, false, null), ct))
                .GetProperty("id").GetGuid();
        var cash = await Account("1000", "Cash on hand", AccountType.Assets, current);
        var bank = await Account("1100", "ABA Bank USD", AccountType.Assets, current);
        var acleda = await Account("1110", "ACLEDA Bank USD", AccountType.Assets, current);
        var capital = await Account("3000", "Owner capital", AccountType.Equity, equity);
        var sales = await Account("4000", "Sales", AccountType.Income, revenue);
        var service = await Account("4100", "Service income", AccountType.Income, revenue);
        var rent = await Account("5000", "Rent", AccountType.CostAndExpense, expenses);
        var salaries = await Account("5100", "Salaries", AccountType.CostAndExpense, expenses);
        var utilities = await Account("5200", "Utilities", AccountType.CostAndExpense, expenses);
        var bankCharges = await Account("5300", "Bank charges", AccountType.CostAndExpense, expenses);
        var vat = chart["2100"];
        var suspense = chart["2999"];
        var clearing = chart["3900"];
        var vat10 = taxes["VAT 10% (sales)"];
        var fuel4 = taxes["VAT 4% fuel (sales)"];

        await banks.CreateAsync(principal, Meta(), orgId, ledgerId, bank, "ABA Bank", "000 123 456", ct);
        await banks.CreateAsync(principal, Meta(), orgId, ledgerId, acleda, "ACLEDA Bank", "0001-01-234567-1-1", ct);
        async Task<Guid> Party(string name, bool customer, string? taxId, int? terms, string? contact) =>
            Data(await partyService.CreateAsync(principal, Meta(), orgId, ledgerId,
                new PartyInput(name, taxId, customer, !customer, null, null, "Phnom Penh", contact, terms, true), ct)).GetProperty("id").GetGuid();
        var customers = new[]
        {
            await Party("Mekong Retail Co., Ltd.", true, "K001-901234567", 30, "Dara"),
            await Party("Angkor Hotels Plc", true, "K002-902345678", 15, "Sophea"),
            await Party("Riverside Café", true, null, 0, null),
        };
        var landlord = await Party("Phnom Penh Property Ltd.", false, "K003-903456789", 15, "Vuthy");
        var edc = await Party("Electricité du Cambodge", false, "E000-100000001", 7, null);
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
        // Purchases (M5): rent with withholding, electricity with and without a valid tax invoice, a debit note,
        // and an invoice dated in the closed period, which stays a draft with the reason.
        var purchases = services.GetRequiredService<PurchaseInvoiceService>();
        PurchaseLineInput Line(Guid account, string description, decimal amount, string? tax, bool valid = true, string? wht = null) =>
            new(account, description, null, null, null, amount, tax is null ? null : taxes[tax], null, null, valid, wht is null ? null : taxes[wht]);
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(landlord, null, DateOnly.Parse("2026-10-01"), null, "PPP-2026-10", "October office rent",
            [Line(rent, "Office rent October", 1_200m, "VAT 10% (purchases)", wht: "WHT 10% rent")]), true, ct);
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(edc, null, DateOnly.Parse("2026-10-12"), null, "EDC-884512", "Electricity September",
            [Line(utilities, "Office electricity", 310m, "VAT 10% (purchases)"), Line(utilities, "Warehouse electricity (no VAT invoice)", 95m, "VAT 10% (purchases)", valid: false)]), true, ct);
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(edc, null, DateOnly.Parse("2026-10-20"), null, "EDC-CR-12", "Meter reading correction",
            [Line(utilities, "Overcharge refund", 20m, "VAT 10% (purchases)")]), true, ct, debitNote: true);
        var ledgerVersion = long.Parse((await companies.GetLedgerAsync(principal, orgId, ledgerId, ct)).Version);
        await settings.CloseAsync(principal, Meta(ledgerVersion), orgId, ledgerId, DateOnly.Parse("2026-09-30"), ct);
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(landlord, null, DateOnly.Parse("2026-09-15"), null, "PPP-2026-09B", "Late September repair bill",
            [Line(rent, "Repairs", 250m, "Non-VAT")]), true, ct);

        return orgId;
    }

    private static WriteMeta Meta(long? version = null) => new(Guid.NewGuid(), Guid.NewGuid().ToString("N"), version, "dev-demo");

    private static JsonElement Data(WriteResult result) =>
        result.Status < 300 ? JsonDocument.Parse(result.Json).RootElement.Clone() : throw new InvalidOperationException($"Demo seed failed: {result.Json}");
}
