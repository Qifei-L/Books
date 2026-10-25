using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>
/// Item codes and the sales and purchase sides of an item (Manager.io, QuickBooks and Xero style): a line naming the
/// item starts from that side's text and price; tax codes must suit the side; codes are unique in the ledger.
/// </summary>
public sealed class ItemDetailsTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lines_naming_the_item_start_from_its_sales_or_purchase_text_and_price()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var item = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/items", new
        {
            name = "Rice 50 kg", code = "RICE-50", unit = "bag",
            sales_price = "32.50", sales_description = "Jasmine rice, 50 kg bag", sales_tax_code_id = s.Taxes["VAT 10% (sales)"],
            purchase_price = "27.00", purchase_description = "Rice from mill", purchase_tax_code_id = s.Taxes["VAT 10% (purchases)"],
        })).DataAsync(HttpStatusCode.Created));
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Mill", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();

        var sale = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-10", lines = new[] { new { account_id = s.Books.Sales, item_id = item.Id(), quantity = "2" } },
        })).DataAsync(HttpStatusCode.Created);
        var purchase = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-05", lines = new[] { new { account_id = s.Services, item_id = item.Id(), quantity = "4" } },
        })).DataAsync(HttpStatusCode.Created);
        var khr = await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-10", currency = "KHR", exchange_rate = "4100",
            lines = new[] { new { account_id = s.Books.Sales, item_id = item.Id(), quantity = "2" } },
        });

        Assert.Equal(("RICE-50", "32.50", "27.00"), (Get(item, "code"), Get(item, "sales_price"), Get(item, "purchase_price")));
        var saleLine = sale.GetProperty("lines")[0];
        Assert.Equal(("Jasmine rice, 50 kg bag", "32.5", "65.00"), (Get(saleLine, "description"), Get(saleLine, "unit_price"), Get(saleLine, "net_amount")));
        var purchaseLine = purchase.GetProperty("lines")[0];
        Assert.Equal(("Rice from mill", "108.00"), (Get(purchaseLine, "description"), Get(purchaseLine, "net_amount")));
        await khr.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");      // USD prices are not used on a KHR invoice
    }

    [Fact]
    public async Task Codes_are_unique_and_tax_codes_must_suit_their_side()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var url = $"{s.LedgerUrl}/items";
        await (await s.Books.Admin.PostAsync(url, new { name = "Rice", code = "RICE-50" })).DataAsync(HttpStatusCode.Created);

        var duplicate = await s.Books.Admin.PostAsync(url, new { name = "Other rice", code = "rice-50" });
        var wrongSide = await s.Books.Admin.PostAsync(url, new { name = "Sugar", sales_tax_code_id = s.Taxes["VAT 10% (purchases)"] });
        var negative = await s.Books.Admin.PostAsync(url, new { name = "Salt", sales_price = "-1" });

        await duplicate.AssertProblemAsync(HttpStatusCode.Conflict, "ITEM_CODE_EXISTS");
        await wrongSide.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await negative.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
