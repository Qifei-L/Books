using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Inventory items with their own accounts (Manager.io style): stock in the item's asset account, sales in its income
/// account and cost in its expense account; once used, the asset account stays, so the stock never splits.
/// </summary>
public sealed class ItemAccountTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_item_buys_sells_and_writes_off_through_its_own_accounts()
    {
        var (s, item, accounts) = await SetUpAsync();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wholesaler", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();

        var bought = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01", lines = new[] { new { account_id = s.Services, item_id = item, quantity = "10", amount = "50.00" } },
        })).DataAsync(HttpStatusCode.Created);
        var sold = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-10",
            lines = new[] { new { account_id = s.Books.Sales, item_id = item, quantity = "4", unit_price = "9.00" } },
        })).DataAsync(HttpStatusCode.Created);
        var writeOff = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/stock-adjustments", new
        {
            date = "2026-10-20", lines = new[] { new { item_id = item, quantity = "-1" } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal("50.00", (await LinesAsync(s, bought))[accounts.Inventory].Dr);
        var sale = await LinesAsync(s, sold);
        Assert.Equal(("36.00", "20.00", "20.00"), (sale[accounts.Income].Cr, sale[accounts.Cost].Dr, sale[accounts.Inventory].Cr));
        Assert.False(sale.ContainsKey(s.Books.Sales));
        var adjustment = await LinesAsync(s, writeOff);
        Assert.Equal(("5.00", "5.00"), (adjustment[accounts.Cost].Dr, adjustment[accounts.Inventory].Cr));
        var report = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory?asOf=2026-10-31", Ct)).DataAsync();
        Assert.Equal(("25.00", true), (report.GetProperty("total_value").GetString(), report.GetProperty("matches_control").GetBoolean()));
    }

    [Fact]
    public async Task Once_used_the_stock_account_stays_and_only_income_and_cost_may_change()
    {
        var (s, item, accounts) = await SetUpAsync();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wholesaler", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01", lines = new[] { new { account_id = s.Services, item_id = item, quantity = "10", amount = "50.00" } },
        })).DataAsync(HttpStatusCode.Created);
        var url = $"{s.LedgerUrl}/items/{item}";
        object Body(Guid? inventory, bool track = true, Guid? income = null) => new
        {
            name = "Bottled water", track_inventory = track, inventory_account_id = inventory, income_account_id = income ?? accounts.Income, cost_of_sales_account_id = accounts.Cost,
        };

        var moved = await s.Books.Admin.WriteAsync(HttpMethod.Put, url, Body(s.Account("1400")), ifMatch: 1);
        var untracked = await s.Books.Admin.WriteAsync(HttpMethod.Put, url, Body(null, track: false), ifMatch: 1);
        var income = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, url, Body(accounts.Inventory, income: s.Books.Sales), ifMatch: 1)).DataAsync();
        var notInventory = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new { name = "Ice", track_inventory = true, inventory_account_id = s.Account("1200") });

        await moved.AssertProblemAsync(HttpStatusCode.Conflict, "INVENTORY_ACCOUNT_LOCKED");
        await untracked.AssertProblemAsync(HttpStatusCode.Conflict, "ITEM_IN_USE");
        Assert.Equal(s.Books.Sales, income.GetProperty("income_account_id").GetGuid());
        await notInventory.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private sealed record Accounts(Guid Inventory, Guid Income, Guid Cost);

    /// <summary>A second inventory account (with the Inventory usage), a beverage income and cost account, and an item using them.</summary>
    private async Task<(SalesInvoiceTests.Setup, Guid Item, Accounts)> SetUpAsync()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        async Task<Guid> Account(string code, string name, string type) => (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, code, name, type)).Id();
        var inventory = await Account("1410", "Inventory, beverages", "assets");
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/accounts/{inventory}/usages", new { usages = new[] { "inventory" } }, ifMatch: 1)).DataAsync();
        var accounts = new Accounts(inventory, await Account("4200", "Beverage sales", "income"), await Account("5810", "Beverage cost", "cost_and_expense"));
        var item = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new
        {
            name = "Bottled water", unit = "case", track_inventory = true,
            inventory_account_id = accounts.Inventory, income_account_id = accounts.Income, cost_of_sales_account_id = accounts.Cost,
        })).DataAsync(HttpStatusCode.Created)).Id();
        return (s, item, accounts);
    }

    private static async Task<Dictionary<Guid, (string Dr, string Cr)>> LinesAsync(SalesInvoiceTests.Setup s, JsonElement document)
    {
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{document.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        static decimal D(JsonElement l, string p) => decimal.Parse(l.GetProperty(p).GetString()!, CultureInfo.InvariantCulture);
        return journal.GetProperty("lines").EnumerateArray().GroupBy(l => l.GetProperty("account_id").GetGuid()).ToDictionary(g => g.Key,
            g => (g.Sum(l => D(l, "debit")).ToString("F2", CultureInfo.InvariantCulture), g.Sum(l => D(l, "credit")).ToString("F2", CultureInfo.InvariantCulture)));
    }
}
