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
        var salaries = chart["6100"];                                        // from the template, like the other payroll accounts
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
        Guid[] customers =
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
        // Purchases (M5): rent with withholding, electricity with creditable and non-creditable VAT, a debit note,
        // and an invoice dated in the closed period, which stays a draft with the reason.
        var purchases = services.GetRequiredService<PurchaseInvoiceService>();
        PurchaseLineInput Line(Guid account, string description, decimal amount, string? tax, string? wht = null) =>
            new(account, description, null, null, null, amount, tax is null ? null : taxes[tax], null, null, wht is null ? null : taxes[wht]);
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(landlord, null, DateOnly.Parse("2026-10-01"), null, "PPP-2026-10", "October office rent",
            [Line(rent, "Office rent October", 1_200m, "VAT 10% (purchases)", wht: "WHT 10% rent")]), true, ct);
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(edc, null, DateOnly.Parse("2026-10-12"), null, "EDC-884512", "Electricity September",
            [Line(utilities, "Office electricity", 310m, "VAT 10% (purchases)"), Line(utilities, "Warehouse electricity (no VAT invoice)", 95m, "VAT 10% (purchases, non-creditable)")]), true, ct);
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(edc, null, DateOnly.Parse("2026-10-20"), null, "EDC-CR-12", "Meter reading correction",
            [Line(utilities, "Overcharge refund", 20m, "VAT 10% (purchases)")]), true, ct, debitNote: true);
        // Sales (M6): invoices with VAT 10% and a zero-rated export service, a credit note, and a draft left for review.
        var salesInvoices = services.GetRequiredService<SalesInvoiceService>();
        SalesLineInput Sale(Guid account, string description, decimal amount, string tax) => new(account, description, null, null, null, amount, taxes[tax], null, null);
        var mekongInvoice = Data(await salesInvoices.CreateAsync(principal, Meta(), orgId, ledgerId, new SalesInvoiceInput(customers[0], null, DateOnly.Parse("2026-10-05"), null,
            "INV-2026-1005", "Goods October", [Sale(sales, "Trading goods", 2_000m, "VAT 10% (sales)")]), true, ct)).GetProperty("id").GetGuid();
        await salesInvoices.CreateAsync(principal, Meta(), orgId, ledgerId, new SalesInvoiceInput(customers[1], null, DateOnly.Parse("2026-10-15"), null,
            "INV-2026-1015", "Advisory October", [Sale(service, "Advisory hours", 1_500m, "VAT 10% (sales)"), Sale(service, "Offshore review (export)", 600m, "VAT 0% (zero-rated)")]), true, ct);
        await salesInvoices.CreateAsync(principal, Meta(), orgId, ledgerId, new SalesInvoiceInput(customers[0], null, DateOnly.Parse("2026-10-22"), null,
            "CN-2026-01", "Damaged goods returned", [Sale(sales, "Return of 10 pcs", 200m, "VAT 10% (sales)")], mekongInvoice), true, ct, creditNote: true);
        await salesInvoices.CreateAsync(principal, Meta(), orgId, ledgerId, new SalesInvoiceInput(customers[2], null, DateOnly.Parse("2026-10-28"), null,
            null, "Catering supplies (draft for review)", [Sale(sales, "Trading goods", 340m, "VAT 10% (sales)")]), false, ct);
        // Inventory (M10): a stocked item bought and sold at average cost, and two damaged cases written off.
        var wholesaler = Data(await partyService.CreateAsync(principal, Meta(), orgId, ledgerId,
            new PartyInput("Kampuchea Beverages Co., Ltd.", "K003-100200300", false, true, null, null, null, null, 30, null), ct)).GetProperty("id").GetGuid();
        var water = Data(await items.CreateAsync(principal, Meta(), orgId, ledgerId,
            new ItemInput("Bottled water", "24 × 500 ml", "case", goods, sales, vat10, TrackInventory: true), ct)).GetProperty("id").GetGuid();
        await purchases.CreateAsync(principal, Meta(), orgId, ledgerId, new PurchaseInvoiceInput(wholesaler, null, DateOnly.Parse("2026-10-03"), null, "KB-55120", "Bottled water",
            [new PurchaseLineInput(chart["1400"], "Bottled water", water, 100m, 4m, null, taxes["VAT 10% (purchases)"], null, null, null)]), true, ct);
        await salesInvoices.CreateAsync(principal, Meta(), orgId, ledgerId, new SalesInvoiceInput(customers[2], null, DateOnly.Parse("2026-10-08"), null,
            "INV-2026-1008", "Bottled water", [new SalesLineInput(sales, "Bottled water", water, 30m, 7m, null, taxes["VAT 10% (sales)"], null, null)]), true, ct);
        await services.GetRequiredService<StockAdjustmentService>().CreateAsync(principal, Meta(), orgId, ledgerId, new StockAdjustmentInput(DateOnly.Parse("2026-10-24"),
            "WO-2610", "Damaged in storage", [new StockAdjustmentLineInput(water, "Crushed cases", -2m, null, null, null)]), true, ct);
        // Payments and receipts (M7), Manager.io style: settle with the party on the A/P or A/R account; a customer's
        // withholding and a bank fee are their own lines.
        var paymentService = services.GetRequiredService<PaymentService>();
        var withheld = await Account("5950", "Tax withheld by customers", AccountType.CostAndExpense, expenses);
        PaymentLineInput Settle(Guid control, decimal amount) => new(null, control, null, null, null, null, amount);
        PaymentLineInput To(Guid account, string description, decimal amount) => new(null, account, description, null, null, null, amount);
        await paymentService.CreateAsync(principal, Meta(), orgId, ledgerId, new PaymentInput(bank, landlord, DateOnly.Parse("2026-10-15"), "TT-1015", "October rent",
            [Settle(chart["2000"], 1_200m), To(bankCharges, "Transfer fee", 2m)]), true, false, ct);
        await paymentService.CreateAsync(principal, Meta(), orgId, ledgerId, new PaymentInput(bank, null, DateOnly.Parse("2026-10-20"), "GDT-09", "VAT for September",
            [To(vat, "VAT payable September", 181m)]), true, false, ct);
        await paymentService.CreateAsync(principal, Meta(), orgId, ledgerId, new PaymentInput(bank, customers[0], DateOnly.Parse("2026-10-26"), "ABA-IN-26", "Mekong Retail payment",
            [Settle(chart["1200"], 1_980m)]), true, true, ct);
        await paymentService.CreateAsync(principal, Meta(), orgId, ledgerId, new PaymentInput(bank, customers[1], DateOnly.Parse("2026-10-29"), "ABA-IN-29", "Angkor Hotels part payment",
            [Settle(chart["1200"], 1_500m), To(withheld, "WHT 15% withheld by customer", -225m)]), true, true, ct);
        // A KHR account (M12.2): riel bought from ACLEDA, and a riel payment for printing.
        var wing = await Account("1120", "Wing Bank KHR", AccountType.Assets, current);
        await banks.CreateAsync(principal, Meta(), orgId, ledgerId, wing, "Wing Bank", "100 200 300", ct, "KHR");
        await transfers.CreateAsync(principal, Meta(), orgId, ledgerId,
            new BankTransferInput(DateOnly.Parse("2026-10-21"), acleda, wing, 500m, 0m, null, "FX-1021", "Buy riel", ToAmount: 2_060_000m), true, ct);
        await paymentService.CreateAsync(principal, Meta(), orgId, ledgerId, new PaymentInput(wing, null, DateOnly.Parse("2026-10-23"), "WING-23", "Printing paid in riel",
            [To(utilities, "Leaflets", 410_000m)]), true, false, ct);
        // Payroll: October results for 40 staff from the payroll system, in KHR, posted as one summary journal.
        string[] given = ["Sokha", "Dara", "Sophea", "Vannak", "Chantha", "Pisey", "Rithy", "Srey Mom", "Visal", "Bopha"];
        string[] family = ["Chan", "Sok", "Kim", "Heng", "Meas"];
        var staff = Enumerable.Range(1, 40).Select(i =>
        {
            var gross = 1_200_000m + i * 35_000m;
            var nssf = Math.Round(Math.Min(gross, 1_200_000m) * 0.02m);
            var tax = Math.Round(Math.Max(0, gross - nssf - 1_500_000m) * 0.05m / 100) * 100;
            return new PayRunLineInput($"E{i:000}", $"{family[i % 5]} {given[i % 10]}", i % 13 != 0, gross, 0, tax, 0, nssf, Math.Round(gross * 0.026m), 0, gross - tax - nssf);
        }).ToList();
        await services.GetRequiredService<PayRunService>().CreateAsync(principal, Meta(), orgId, ledgerId,
            new PayRunInput(new DateOnly(2026, 10, 1), DateOnly.Parse("2026-10-31"), "PAY-2610", "October payroll", null, staff, "KHR"), true, ct);
        // Everything up to 14 October was reconciled before; the October statement starts from that balance.
        var reconciliation = services.GetRequiredService<BankReconciliationService>();
        var reconciled = await reconciliation.ReconciliationAsync(principal, orgId, ledgerId, bank, DateOnly.Parse("2026-10-14"), null, ct);
        await reconciliation.ClearAsync(principal, Meta(), orgId, ledgerId, bank, DateOnly.Parse("2026-10-14"), reconciled.Pending.Select(p => p.JournalId).ToList(), ct);
        var opening = decimal.Parse(reconciled.BookBalance, System.Globalization.CultureInfo.InvariantCulture);
        var statements = services.GetRequiredService<BankStatementService>();
        await statements.ImportAsync(principal, Meta(), orgId, ledgerId, bank, opening, opening + 1_502m,
        [
            new StatementLineInput(DateOnly.Parse("2026-10-16"), "TT PHNOM PENH PROPERTY", "TT-1015", -1_202m, "ABA-2610-0001"),
            new StatementLineInput(DateOnly.Parse("2026-10-20"), "GDT TAX PAYMENT", null, -181m, "ABA-2610-0002"),
            new StatementLineInput(DateOnly.Parse("2026-10-27"), "MEKONG RETAIL CO", null, 1_980m, "ABA-2610-0003"),
            new StatementLineInput(DateOnly.Parse("2026-10-30"), "INCOMING TT ANGKOR HOTELS", null, 1_275m, "ABA-2610-0004"),
            new StatementLineInput(DateOnly.Parse("2026-10-31"), "MONTHLY ACCOUNT FEE", null, -5m, "ABA-2610-0005"),
            new StatementLineInput(DateOnly.Parse("2026-10-31"), "LOAN INTEREST", null, -365m, "ABA-2610-0006"),
        ], ct);
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
