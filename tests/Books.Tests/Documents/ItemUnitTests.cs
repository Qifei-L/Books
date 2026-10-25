using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>
/// Units of measure (M10.8, owner decision: a base unit and other units as multiples of it): lines may be in any of
/// the item's units, prices follow the unit, and stock is always counted in the base unit.
/// </summary>
public sealed class ItemUnitTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Boxes_bought_and_bottles_or_boxes_sold_count_in_bottles()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var item = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new
        {
            name = "Bottled water", unit = "bottle", track_inventory = true, purchase_price = "0.50", sales_price = "0.80",
            units = new[] { new { name = "box", factor = "24" } },
        })).DataAsync(HttpStatusCode.Created);
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wholesaler", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();

        var bought = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01", lines = new[] { new { account_id = s.Services, item_id = item.Id(), quantity = "10", unit = "box" } },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-05", lines = new[] { new { account_id = s.Books.Sales, item_id = item.Id(), quantity = "30" } },
        })).DataAsync(HttpStatusCode.Created);
        var boxes = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-06", lines = new[] { new { account_id = s.Books.Sales, item_id = item.Id(), quantity = "2", unit = "BOX" } },
        })).DataAsync(HttpStatusCode.Created);
        var unknown = await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-06", lines = new[] { new { account_id = s.Books.Sales, item_id = item.Id(), quantity = "1", unit = "pallet" } },
        });
        var noItem = await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-06", lines = new[] { new { account_id = s.Books.Sales, quantity = "1", unit_price = "1", unit = "box" } },
        });

        Assert.Equal("box 24", string.Join(";", item.GetProperty("units").EnumerateArray().Select(u => $"{Get(u, "name")} {Get(u, "factor")}")));
        var line = bought.GetProperty("lines")[0];
        Assert.Equal(("box", "12", "120.00"), (Get(line, "unit"), Get(line, "unit_price"), Get(line, "net_amount")));   // 0.50 × 24 a box
        Assert.Equal(("19.2", "38.40"), (Get(boxes.GetProperty("lines")[0], "unit_price"), Get(boxes.GetProperty("lines")[0], "net_amount")));
        var report = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory?asOf=2026-10-31", Ct)).DataAsync();
        var row = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(("240", "78", "162", "0.50", "81.00"), (Get(row, "quantity_in"), Get(row, "quantity_out"), Get(row, "on_hand"), Get(row, "average_cost"), Get(row, "value")));
        await unknown.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await noItem.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task Units_need_a_base_unit_distinct_names_and_positive_factors()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var url = $"{s.LedgerUrl}/items";

        var noBase = await s.Books.Admin.PostAsync(url, new { name = "Rice", units = new[] { new { name = "bag", factor = "50" } } });
        var sameAsBase = await s.Books.Admin.PostAsync(url, new { name = "Rice", unit = "kg", units = new[] { new { name = "KG", factor = "1" } } });
        var zero = await s.Books.Admin.PostAsync(url, new { name = "Rice", unit = "kg", units = new[] { new { name = "bag", factor = "0" } } });

        await noBase.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await sameAsBase.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await zero.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
