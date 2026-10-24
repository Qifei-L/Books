using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Documents;

/// <summary>
/// Property checks over random purchase documents (fixed seeds so failures replay): per-line tax and
/// withholding add up to the document and to the tax accounts; a document posts with its journal or not at
/// all; AP aging equals the payable balance and each supplier statement (rule 43).
/// </summary>
public sealed class PurchasePropertyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Doc(JsonElement Body, int Sign);

    [Theory]
    [InlineData(11)]
    [InlineData(29)]
    [InlineData(503)]
    public async Task Random_purchases_add_up_everywhere(int seed)
    {
        var random = new Random(seed);
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        Guid[] suppliers =
        [
            s.Supplier,
            await PartyAsync(s, new { name = "Electricité du Cambodge", is_supplier = true, payment_terms_days = 7 }),
            await PartyAsync(s, new { name = "Lucky Supermarket", is_supplier = true }),
        ];
        string?[] taxes = ["VAT 10% (purchases)", "VAT 0% (zero-rated)", "Non-VAT", null];
        string?[] withholdings = ["WHT 10% rent", "WHT 15% services, interest, royalties", null, null];
        await new JournalEditTests(host).CloseThroughAsync(s.Books, "2026-08-31");

        var docs = new List<Doc>();
        for (var i = 0; i < 12; i++)
        {
            var debitNote = random.Next(5) == 0;
            var lines = Enumerable.Range(0, random.Next(1, 4)).Select(_ =>
            {
                var tax = taxes[random.Next(taxes.Length)];
                var wht = withholdings[random.Next(withholdings.Length)];
                return new
                {
                    account_id = random.Next(2) == 0 ? s.Rent : s.Supplies,
                    amount = (random.Next(100, 100_000) / 100m).ToString("0.00", CultureInfo.InvariantCulture),
                    tax_code_id = tax is null ? (Guid?)null : s.Taxes[tax],
                    valid_tax_invoice = random.Next(3) > 0,
                    withholding_code_id = wht is null ? (Guid?)null : s.Taxes[wht],
                };
            }).ToArray();
            var date = new DateOnly(2026, 7, 1).AddDays(random.Next(0, 123));
            var response = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/{(debitNote ? "debit-notes" : "purchase-invoices")}",
                new { supplier_id = suppliers[random.Next(suppliers.Length)], date = date.ToString("yyyy-MM-dd"), lines });
            var closed = date <= new DateOnly(2026, 8, 31);
            var body = await response.DataAsync(closed ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.Created);
            docs.Add(new Doc(body, debitNote ? -1 : 1));
        }

        decimal deductible = 0, nonDeductible = 0, withheld = 0, payable = 0;
        foreach (var (doc, sign) in docs)
        {
            var lines = doc.GetProperty("lines").EnumerateArray().ToList();
            foreach (var line in lines)
            {
                var net = Dec(line, "net_amount");
                var rate = line.GetProperty("tax_code_id").ValueKind == JsonValueKind.Null || line.GetProperty("tax_code_id").GetGuid() != s.Taxes["VAT 10% (purchases)"] ? 0m : 0.10m;
                Assert.Equal(Math.Round(net * rate, 2, MidpointRounding.AwayFromZero), Dec(line, "tax_amount"));
            }
            Assert.Equal(lines.Sum(l => Dec(l, "tax_amount")), Dec(doc, "tax_total"));
            Assert.Equal(lines.Sum(l => Dec(l, "withholding_amount")), Dec(doc, "withholding_total"));
            Assert.Equal(Dec(doc, "net_total") + Dec(doc, "tax_total") - Dec(doc, "withholding_total"), Dec(doc, "payable_total"));

            var posted = doc.GetProperty("status").GetString() == "posted";
            Assert.Equal(posted, doc.GetProperty("journal_id").ValueKind != JsonValueKind.Null);
            if (!posted)
            {
                Assert.Contains(doc.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "DATE_CLOSED");
                continue;
            }
            deductible += sign * lines.Where(l => l.GetProperty("valid_tax_invoice").GetBoolean()).Sum(l => Dec(l, "tax_amount"));
            nonDeductible += sign * lines.Where(l => !l.GetProperty("valid_tax_invoice").GetBoolean()).Sum(l => Dec(l, "tax_amount"));
            withheld += sign * Dec(doc, "withholding_total");
            payable += sign * Dec(doc, "payable_total");
        }

        // Only effective postings reach the accounts: drafts left nothing behind.
        Assert.Equal(deductible, await ClosingAsync(s, "1300"));
        Assert.Equal(nonDeductible, await ClosingAsync(s, "5900"));
        Assert.Equal(-withheld, await ClosingAsync(s, "2200"));
        Assert.Equal(-payable, await ClosingAsync(s, "2000"));

        var aging = await ReportAsync(s, "ap-aging?asOf=2026-10-31");
        Assert.True(aging.GetProperty("matches_control").GetBoolean());
        Assert.Equal(payable, Dec(aging.GetProperty("totals"), "total"));
        foreach (var row in aging.GetProperty("rows").EnumerateArray())
        {
            var buckets = new[] { "not_due", "days1_to30", "days31_to60", "days61_to90", "over90" }.Sum(b => Dec(row, b));
            Assert.Equal(Dec(row, "total"), buckets);
            var statement = await ReportAsync(s, $"supplier-statement?supplierId={row.GetProperty("supplier_id").GetGuid()}&from=2026-01-01&to=2026-10-31");
            Assert.Equal(Dec(row, "total"), Dec(statement, "closing_balance"));
        }
    }

    private static async Task<decimal> ClosingAsync(PurchaseInvoiceTests.Setup s, string code) =>
        Dec(await ReportAsync(s, $"gl-detail?accountId={s.Account(code)}&from=2026-01-01&to=2026-10-31"), "closing_balance");

    private static async Task<JsonElement> ReportAsync(PurchaseInvoiceTests.Setup s, string report) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/{report}", Ct)).DataAsync();

    private static async Task<Guid> PartyAsync(PurchaseInvoiceTests.Setup s, object body) =>
        (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", body)).DataAsync(HttpStatusCode.Created)).Id();

    private static decimal Dec(JsonElement e, string property) => decimal.Parse(e.GetProperty(property).GetString()!, CultureInfo.InvariantCulture);
}
