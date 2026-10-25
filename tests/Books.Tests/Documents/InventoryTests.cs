using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>Inventory items (owner default: average cost, Manager.io style): purchases into stock, sales out at average cost.</summary>
public sealed class InventoryTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sales_take_the_average_cost_and_the_stock_report_equals_the_inventory_account()
    {
        var (s, supplier, item) = await SetUpAsync();
        await BuyAsync(s, supplier, item, "2026-10-01", "10", "50.00");
        await BuyAsync(s, supplier, item, "2026-10-05", "10", "70.00");

        var sale = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-10",
            lines = new[] { new { account_id = s.Books.Sales, item_id = item, quantity = "5", unit_price = "20.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/credit-notes", new
        {
            customer_id = s.Customer, date = "2026-10-20",
            lines = new[] { new { account_id = s.Books.Sales, item_id = item, quantity = "1", unit_price = "20.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);

        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{sale.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        var lines = journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => (Get(l, "debit"), Get(l, "credit")));
        Assert.Equal(("30.00", "30.00"), (lines[s.Account("5800")].Item1, lines[s.Account("1400")].Item2));      // 5 × average 6
        var report = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory?asOf=2026-10-31", Ct)).DataAsync();
        var row = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(("21", "5", "16", "6.00", "96.00"), (Get(row, "quantity_in"), Get(row, "quantity_out"), Get(row, "on_hand"), Get(row, "average_cost"), Get(row, "value")));
        Assert.True(report.GetProperty("matches_control").GetBoolean());
    }

    [Fact]
    public async Task An_inventory_item_needs_a_quantity_and_is_bought_into_the_inventory_account()
    {
        var (s, supplier, item) = await SetUpAsync();

        var missing = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01", lines = new[] { new { account_id = s.Services, item_id = item, amount = "50.00" } },
        });
        var bought = await BuyAsync(s, supplier, item, "2026-10-01", "10", "50.00");

        await missing.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(s.Account("1400"), bought.GetProperty("lines")[0].GetProperty("account_id").GetGuid());
    }

    private async Task<(SalesInvoiceTests.Setup, Guid Supplier, Guid Item)> SetUpAsync()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wholesaler", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        var item = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new { name = "Bottled water", unit = "case", track_inventory = true }))
            .DataAsync(HttpStatusCode.Created)).Id();
        return (s, supplier, item);
    }

    private static async Task<JsonElement> BuyAsync(SalesInvoiceTests.Setup s, Guid supplier, Guid item, string date, string quantity, string amount) =>
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date,
            lines = new[] { new { account_id = s.Services, item_id = item, quantity, amount, tax_code_id = s.Taxes["VAT 10% (purchases)"] } },
        })).DataAsync(HttpStatusCode.Created);

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
