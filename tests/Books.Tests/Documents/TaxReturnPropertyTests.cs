using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Property checks over random sales and purchases in one month (fixed seeds so failures replay): every line box
/// equals the sum of its drilldown in USD and KHR, formula boxes follow their inputs, and the tax accounts reconcile.
/// </summary>
public sealed class TaxReturnPropertyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(3)]
    [InlineData(58)]
    [InlineData(612)]
    public async Task Boxes_drilldowns_and_tax_accounts_agree(int seed)
    {
        var random = new Random(seed);
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Supplier", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        var expense = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "5000", "Expenses", "cost_and_expense")).Id();
        string?[] salesTaxes = ["VAT 10% (sales)", "VAT 0% (zero-rated)", "Non-VAT", null];
        string?[] purchaseTaxes = ["VAT 10% (purchases)", "VAT 0% (zero-rated)", "Non-VAT", null];
        string?[] withholding = ["WHT 15% services, interest, royalties", "WHT 10% rent", "WHT 14% non-resident", null, null];
        string Amount() => (random.Next(100, 100_000) / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        Guid? Code(string? name) => name is null ? null : s.Taxes[name];

        for (var i = 0; i < 14; i++)
        {
            var date = new DateOnly(2026, 10, random.Next(1, 32)).ToString("yyyy-MM-dd");
            var lineCount = random.Next(1, 4);
            if (random.Next(2) == 0)
            {
                var path = random.Next(5) == 0 ? "credit-notes" : "sales-invoices";
                await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/{path}", new
                {
                    customer_id = s.Customer, date,
                    lines = Enumerable.Range(0, lineCount).Select(_ => new { account_id = s.Books.Sales, amount = Amount(), tax_code_id = Code(salesTaxes[random.Next(salesTaxes.Length)]) }).ToArray(),
                })).DataAsync(HttpStatusCode.Created);
            }
            else
            {
                var path = random.Next(5) == 0 ? "debit-notes" : "purchase-invoices";
                await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/{path}", new
                {
                    supplier_id = supplier, date,
                    lines = Enumerable.Range(0, lineCount).Select(_ => new
                    {
                        account_id = expense, amount = Amount(), tax_code_id = Code(purchaseTaxes[random.Next(purchaseTaxes.Length)]),
                        valid_tax_invoice = random.Next(3) > 0, withholding_code_id = Code(withholding[random.Next(withholding.Length)]),
                    }).ToArray(),
                })).DataAsync(HttpStatusCode.Created);
            }
        }

        var october = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10");
        var boxes = october.GetProperty("boxes").EnumerateArray().ToDictionary(b => b.GetProperty("key").GetString()!);
        decimal Usd(string key) => Dec(boxes[key], "amount");
        decimal Khr(string key) => Dec(boxes[key], "tax_currency_amount");

        foreach (var box in boxes.Values.Where(b => b.GetProperty("lines").GetInt32() > 0))
        {
            var key = box.GetProperty("key").GetString()!;
            var lines = (await TaxReturnTests.GetAsync(s, $"tax-returns/2026-10/boxes/{key}")).GetProperty("lines").EnumerateArray().ToList();
            Assert.Equal(Usd(key), lines.Sum(l => Dec(l, "amount")));
            // KHR is rounded once per box from the unrounded line amounts at 4100.
            Assert.Equal(Math.Round(Usd(key) * 4100m, 0, MidpointRounding.AwayFromZero), Khr(key));
        }
        Assert.Equal(Usd("t701_13") + Usd("t701_15"), Usd("t701_19"));
        Assert.Equal(Usd("t701_19") - Usd("t701_11"), Usd("t701_vat_payable"));
        Assert.Equal(Khr("t701_19") - Khr("t701_11"), Khr("t701_vat_payable"));
        Assert.Equal(new[] { "t701_12", "t701_14", "t701_16", "t701_17", "t701_18" }.Sum(Usd), Usd("t701_turnover"));
        Assert.Equal(Math.Round(Usd("t701_turnover") / 100m, 2, MidpointRounding.AwayFromZero), Usd("t701_ppi_01"));
        Assert.Equal(Usd("wt03_r1_services") + Usd("wt03_r5_rent") + Usd("wt03_n3_management"), Usd("wt03_total"));
        Assert.All(october.GetProperty("reconciliation").EnumerateArray(), r => Assert.True(r.GetProperty("matches").GetBoolean(), r.GetRawText()));
    }

    private static decimal Dec(JsonElement e, string property) => decimal.Parse(e.GetProperty(property).GetString()!, CultureInfo.InvariantCulture);
}
