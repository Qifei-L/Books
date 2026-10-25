using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>
/// Documents in another currency (M12): amounts stay in the document's currency, the posting converts each amount at
/// its rate, the control account takes the rounding, and aging, KHR totals and the return follow the posting.
/// </summary>
public sealed class CurrencyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_khr_sales_invoice_posts_in_usd_and_keeps_its_own_khr_on_the_tax_return()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15", currency = "KHR", exchange_rate = "4000",
            lines = new[] { new { account_id = s.Books.Sales, amount = "400000", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("KHR", "4000", "440000.00", "110.00"), (Get(invoice, "currency"), Get(invoice, "exchange_rate"), Get(invoice, "gross_total"), Get(invoice, "base_total")));
        var khr = invoice.GetProperty("tax_currency");
        Assert.Equal(("440000", "40000", "Document"), (Get(khr, "gross_total"), Get(khr, "tax_total"), Get(khr, "rate_source")));
        var lines = await LinesAsync(s.Books, invoice);
        Assert.Equal(("100.00", "10.00", "110.00"), (lines[s.Books.Sales].Cr, lines[s.Account("2100")].Cr, lines[s.Account("1200")].Dr));
        var aging = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/ar-aging?asOf=2026-10-31", Ct)).DataAsync();
        Assert.Equal(("110.00", true), (Get(aging.GetProperty("totals"), "total"), aging.GetProperty("matches_control").GetBoolean()));
        var boxes = TaxReturnTests.Boxes(await TaxReturnTests.GetAsync(s, "tax-returns/2026-10"));
        Assert.Equal(("10.00", "40000"), boxes["t701_15"]);
    }

    [Fact]
    public async Task Rounding_from_converting_each_line_goes_to_the_payable_and_the_journal_balances()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{s.Books.OrgId}/exchange-rates",
            new { date = "2026-10-01", from_currency = "USD", to_currency = "KHR", rate = "4100", source = "NBC" })).DataAsync();

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            supplier_id = s.Supplier, date = "2026-10-15", currency = "KHR",
            lines = Enumerable.Range(0, 3).Select(_ => new { account_id = s.Rent, amount = "10001", tax_code_id = s.Taxes["VAT 10% (purchases)"] }).ToArray(),
        })).DataAsync(HttpStatusCode.Created);

        // Each 10,001 KHR is 2.44 USD and each 1,000 KHR of VAT 0.24; the payable takes 7.32 + 0.72.
        var lines = await LinesAsync(s.Books, invoice);
        Assert.Equal(("4100", "7.32", "0.72", "8.04", "8.04"),
            (Get(invoice, "exchange_rate"), lines[s.Rent].Dr, lines[s.Account("1300")].Dr, lines[s.Account("2000")].Cr, Get(invoice, "base_total")));
        var noRate = await s.Books.Admin.PostAsync(s.Url, new
        {
            supplier_id = s.Supplier, date = "2026-10-15", currency = "THB", lines = new[] { new { account_id = s.Rent, amount = "100" } },
        });
        await noRate.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    private static async Task<Dictionary<Guid, (string Dr, string Cr)>> LinesAsync(Journal.JournalBooks books, JsonElement document)
    {
        var journal = await (await books.Admin.GetAsync($"{books.JournalsUrl}/{document.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        static decimal D(JsonElement l, string p) => decimal.Parse(Get(l, p), System.Globalization.CultureInfo.InvariantCulture);
        return journal.GetProperty("lines").EnumerateArray().GroupBy(l => l.GetProperty("account_id").GetGuid()).ToDictionary(g => g.Key,
            g => (g.Sum(l => D(l, "debit")).ToString("F2", System.Globalization.CultureInfo.InvariantCulture), g.Sum(l => D(l, "credit")).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
