using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Documents;

/// <summary>
/// Property checks over random sales documents (fixed seeds so failures replay): per-line VAT adds up to the
/// document, to output VAT and to KHR totals; a document posts with its journal or not at all; AR aging equals
/// the receivable balance and each customer statement (rule 43).
/// </summary>
public sealed class SalesPropertyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(7)]
    [InlineData(41)]
    [InlineData(977)]
    public async Task Random_sales_add_up_everywhere(int seed)
    {
        var random = new Random(seed);
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        Guid[] customers =
        [
            s.Customer,
            await PartyAsync(s, new { name = "Mekong Retail Co., Ltd.", is_customer = true, payment_terms_days = 30 }),
            await PartyAsync(s, new { name = "Riverside Café", is_customer = true }),
        ];
        string?[] taxes = ["VAT 10% (sales)", "VAT 0% (zero-rated)", "Non-VAT", null];
        await new JournalEditTests(host).CloseThroughAsync(s.Books, "2026-08-31");

        var docs = new List<(JsonElement Body, int Sign)>();
        for (var i = 0; i < 12; i++)
        {
            var creditNote = random.Next(5) == 0;
            var lines = Enumerable.Range(0, random.Next(1, 4)).Select(_ =>
            {
                var tax = taxes[random.Next(taxes.Length)];
                return new
                {
                    account_id = random.Next(2) == 0 ? s.Books.Sales : s.Services,
                    amount = (random.Next(100, 100_000) / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                    tax_code_id = tax is null ? (Guid?)null : s.Taxes[tax],
                };
            }).ToArray();
            var date = new DateOnly(2026, 7, 1).AddDays(random.Next(0, 123));
            var response = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/{(creditNote ? "credit-notes" : "sales-invoices")}",
                new { customer_id = customers[random.Next(customers.Length)], date = date.ToString("yyyy-MM-dd"), lines });
            var closed = date <= new DateOnly(2026, 8, 31);
            docs.Add((await response.DataAsync(closed ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.Created), creditNote ? -1 : 1));
        }

        decimal outputVat = 0, receivable = 0;
        foreach (var (doc, sign) in docs)
        {
            var lines = doc.GetProperty("lines").EnumerateArray().ToList();
            foreach (var line in lines)
            {
                var vat10 = line.GetProperty("tax_code_id").ValueKind != JsonValueKind.Null && line.GetProperty("tax_code_id").GetGuid() == s.Taxes["VAT 10% (sales)"];
                Assert.Equal(Math.Round(Dec(line, "net_amount") * (vat10 ? 0.10m : 0m), 2, MidpointRounding.AwayFromZero), Dec(line, "tax_amount"));
            }
            Assert.Equal(lines.Sum(l => Dec(l, "tax_amount")), Dec(doc, "tax_total"));
            Assert.Equal(Dec(doc, "net_total") + Dec(doc, "tax_total"), Dec(doc, "gross_total"));
            // KHR at the setup's rate of 4100, rounded to whole riel.
            var khr = doc.GetProperty("tax_currency");
            Assert.Equal(Math.Round(Dec(doc, "gross_total") * 4100m, 0, MidpointRounding.AwayFromZero), Dec(khr, "gross_total"));
            Assert.Equal(Math.Round(Dec(doc, "tax_total") * 4100m, 0, MidpointRounding.AwayFromZero), Dec(khr, "tax_total"));

            var posted = doc.GetProperty("status").GetString() == "posted";
            Assert.Equal(posted, doc.GetProperty("journal_id").ValueKind != JsonValueKind.Null);
            if (!posted)
            {
                Assert.Contains(doc.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "DATE_CLOSED");
                continue;
            }
            outputVat += sign * Dec(doc, "tax_total");
            receivable += sign * Dec(doc, "gross_total");
        }

        // Only effective postings reach the accounts: drafts left nothing behind.
        Assert.Equal(-outputVat, await ClosingAsync(s, "2100"));
        Assert.Equal(receivable, await ClosingAsync(s, "1200"));

        var aging = await ReportAsync(s, "ar-aging?asOf=2026-10-31");
        Assert.True(aging.GetProperty("matches_control").GetBoolean());
        Assert.Equal(receivable, Dec(aging.GetProperty("totals"), "total"));
        foreach (var row in aging.GetProperty("rows").EnumerateArray())
        {
            var statement = await ReportAsync(s, $"customer-statement?customerId={row.GetProperty("customer_id").GetGuid()}&from=2026-01-01&to=2026-10-31");
            Assert.Equal(Dec(row, "total"), Dec(statement, "closing_balance"));
        }
    }

    private static async Task<decimal> ClosingAsync(SalesInvoiceTests.Setup s, string code) =>
        Dec(await ReportAsync(s, $"gl-detail?accountId={s.Account(code)}&from=2026-01-01&to=2026-10-31"), "closing_balance");

    private static async Task<JsonElement> ReportAsync(SalesInvoiceTests.Setup s, string report) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/{report}", Ct)).DataAsync();

    private static async Task<Guid> PartyAsync(SalesInvoiceTests.Setup s, object body) =>
        (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", body)).DataAsync(HttpStatusCode.Created)).Id();

    private static decimal Dec(JsonElement e, string property) => decimal.Parse(e.GetProperty(property).GetString()!, CultureInfo.InvariantCulture);
}
