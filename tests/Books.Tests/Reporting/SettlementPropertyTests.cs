using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;

namespace Books.Tests.Reporting;

/// <summary>
/// Property checks over random invoices, notes, payments and refunds (fixed seeds so failures replay): settlement by
/// party keeps AP aging equal to the payable balance and to each supplier's statement on every date.
/// </summary>
public sealed class SettlementPropertyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(5)]
    [InlineData(77)]
    [InlineData(401)]
    public async Task Aging_equals_the_payable_balance_and_the_statements_on_every_date(int seed)
    {
        var random = new Random(seed);
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        Guid[] suppliers =
        [
            s.Supplier,
            (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Second supplier", is_supplier = true, payment_terms_days = 15 })).DataAsync(HttpStatusCode.Created)).Id(),
        ];
        var invoices = new List<(Guid Id, Guid Supplier)>();
        string Amount(int max) => (random.Next(100, max) / 100m).ToString("0.00", CultureInfo.InvariantCulture);

        for (var i = 0; i < 18; i++)
        {
            var supplier = suppliers[random.Next(suppliers.Length)];
            var date = new DateOnly(2026, 7, 1).AddDays(random.Next(0, 120)).ToString("yyyy-MM-dd");
            switch (random.Next(6))
            {
                case 0 or 1 or 2:
                    var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
                    {
                        supplier_id = supplier, date, lines = new[] { new { account_id = s.Rent, amount = Amount(50_000), tax_code_id = s.Taxes["VAT 10% (purchases)"] } },
                    })).DataAsync(HttpStatusCode.Created);
                    invoices.Add((invoice.Id(), supplier));
                    break;
                case 3:
                    await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/debit-notes", new
                    {
                        supplier_id = supplier, date, lines = new[] { new { account_id = s.Rent, amount = Amount(5_000) } },
                    })).DataAsync(HttpStatusCode.Created);
                    break;
                case 4:
                    var named = invoices.Where(x => x.Supplier == supplier).Select(x => (Guid?)x.Id).LastOrDefault();
                    object line = named is { } id && random.Next(2) == 0
                        ? new { invoice_id = id, amount = Amount(40_000) }
                        : new { account_id = s.Account("2000"), amount = Amount(40_000) };
                    await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new { bank_account_id = bank, party_id = supplier, date, lines = new[] { line } }))
                        .DataAsync(HttpStatusCode.Created);
                    break;
                default:
                    // A refund from the supplier: a receipt on the payable account.
                    await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
                    {
                        bank_account_id = bank, party_id = supplier, date, lines = new[] { new { account_id = s.Account("2000"), amount = Amount(3_000) } },
                    })).DataAsync(HttpStatusCode.Created);
                    break;
            }
        }

        foreach (var asOf in new[] { "2026-08-15", "2026-09-30", "2026-10-31" })
        {
            var aging = await GetAsync(s, $"ap-aging?asOf={asOf}");
            Assert.True(aging.GetProperty("matches_control").GetBoolean(), aging.GetRawText());
            foreach (var row in aging.GetProperty("rows").EnumerateArray())
            {
                var buckets = new[] { "not_due", "days1_to30", "days31_to60", "days61_to90", "over90" }.Sum(b => Dec(row, b));
                Assert.Equal(Dec(row, "total"), buckets);
                var statement = await GetAsync(s, $"supplier-statement?supplierId={row.GetProperty("supplier_id").GetGuid()}&from=2026-01-01&to={asOf}");
                Assert.Equal(Dec(row, "total"), Dec(statement, "closing_balance"));
            }
        }
    }

    private static async Task<JsonElement> GetAsync(PurchaseInvoiceTests.Setup s, string report) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/{report}", Ct)).DataAsync();

    private static decimal Dec(JsonElement e, string property) => decimal.Parse(e.GetProperty(property).GetString()!, CultureInfo.InvariantCulture);
}
