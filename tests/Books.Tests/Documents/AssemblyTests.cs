using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>Assemblies (M10.10, owner decision: components at average cost plus labour).</summary>
public sealed class AssemblyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_gift_box_is_made_from_its_components_plus_labour()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        async Task<Guid> Item(object body) => (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", body)).DataAsync(HttpStatusCode.Created)).Id();
        var water = await Item(new { name = "Water", unit = "bottle", track_inventory = true });
        var basket = await Item(new { name = "Basket", unit = "pcs", track_inventory = true });
        var box = await Item(new { name = "Gift box", unit = "pcs", track_inventory = true,
            components = new object[] { new { item_id = water, quantity = "6" }, new { item_id = basket, quantity = "1" } } });
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wholesaler", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01",
            lines = new object[] { new { account_id = s.Services, item_id = water, quantity = "60", amount = "30.00" }, new { account_id = s.Services, item_id = basket, quantity = "10", amount = "20.00" } },
        })).DataAsync(HttpStatusCode.Created);
        var wages = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "6150", "Packing wages", "cost_and_expense")).Id();

        var made = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/assemblies", new
        {
            date = "2026-10-05", item_id = box, quantity = "4", labour = "8.00", labour_account_id = wages,
        })).DataAsync(HttpStatusCode.Created);
        var noBom = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/assemblies", new { date = "2026-10-05", item_id = water, quantity = "1" });

        // 24 bottles at 0.50 = 12.00, 4 baskets at 2.00 = 8.00, labour 8.00: 28.00 for 4 boxes.
        Assert.Equal(("28.00", "7.00", "8.00"), (made.GetProperty("value").GetString(), made.GetProperty("unit_cost").GetString(), made.GetProperty("labour").GetString()));
        var report = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/inventory?asOf=2026-10-31", Ct)).DataAsync();
        var rows = report.GetProperty("items").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!, r => (r.GetProperty("on_hand").GetString(), r.GetProperty("value").GetString()));
        Assert.Equal(("36", "18.00"), rows["Water"]);
        Assert.Equal(("6", "12.00"), rows["Basket"]);
        Assert.Equal(("4", "28.00"), rows["Gift box"]);
        Assert.True(report.GetProperty("matches_control").GetBoolean());
        await noBom.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }
}
