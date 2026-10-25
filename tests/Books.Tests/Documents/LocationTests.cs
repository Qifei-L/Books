using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>
/// Inventory locations (M10.9, owner decision: one average cost across locations): quantities per location, the
/// default location for lines naming none, transfers moving quantity only, and counts per location.
/// </summary>
public sealed class LocationTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stock_is_kept_per_location_and_transfers_move_quantity_without_value()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var url = $"{s.LedgerUrl}/locations";
        var main = (await (await s.Books.Admin.PostAsync(url, new { name = "Main warehouse" })).DataAsync(HttpStatusCode.Created));
        var shop = (await (await s.Books.Admin.PostAsync(url, new { name = "Riverside shop" })).DataAsync(HttpStatusCode.Created));
        var item = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new { name = "Water", unit = "case", track_inventory = true })).DataAsync(HttpStatusCode.Created)).Id();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wholesaler", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();

        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01", lines = new[] { new { account_id = s.Services, item_id = item, quantity = "10", amount = "50.00" } },
        })).DataAsync(HttpStatusCode.Created);                                                  // no location: the default (main)
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/stock-transfers", new
        {
            date = "2026-10-02", from_location_id = main.Id(), to_location_id = shop.Id(), lines = new[] { new { item_id = item, quantity = "4" } },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-03",
            lines = new[] { new { account_id = s.Books.Sales, item_id = item, quantity = "1", unit_price = "9.00", location_id = shop.Id() } },
        })).DataAsync(HttpStatusCode.Created);
        var count = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/stock-adjustments", new
        {
            date = "2026-10-04", lines = new[] { new { item_id = item, counted_quantity = "2", location_id = shop.Id() } },
        })).DataAsync(HttpStatusCode.Created);                                                  // shop had 3: one missing
        var same = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/stock-transfers", new
        {
            date = "2026-10-05", from_location_id = shop.Id(), to_location_id = shop.Id(), lines = new[] { new { item_id = item, quantity = "1" } },
        });

        Assert.True(main.GetProperty("is_default").GetBoolean());
        Assert.False(shop.GetProperty("is_default").GetBoolean());
        Assert.Equal(("3", "-1", "-5.00"), (count.GetProperty("lines")[0].GetProperty("book_quantity").GetString(), count.GetProperty("lines")[0].GetProperty("quantity").GetString(),
            count.GetProperty("lines")[0].GetProperty("value").GetString()));
        var stock = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory/locations?asOf=2026-10-31", Ct)).DataAsync();
        Assert.Equal(["Main warehouse 6", "Riverside shop 2"], stock.GetProperty("rows").EnumerateArray().Select(r => $"{r.GetProperty("location").GetString()} {r.GetProperty("quantity").GetString()}"));
        var report = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory?asOf=2026-10-31", Ct)).DataAsync();
        var row = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(("8", "40.00", true), (row.GetProperty("on_hand").GetString(), row.GetProperty("value").GetString(), report.GetProperty("matches_control").GetBoolean()));
        await same.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }
}
