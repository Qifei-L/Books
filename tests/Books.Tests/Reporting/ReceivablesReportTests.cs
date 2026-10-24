using System.Net;
using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;

namespace Books.Tests.Reporting;

/// <summary>Rule 43 for sales: AR aging by customer and due date equals the Receivable control balance; customer statements.</summary>
public sealed class ReceivablesReportTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Aging_buckets_by_days_past_due_and_equals_the_receivable_balance()
    {
        var s = await SeedAsync();

        var aging = await GetAsync(s, "ar-aging?asOf=2026-11-30");

        var row = Assert.Single(aging.GetProperty("rows").EnumerateArray());
        // The credit note settles the oldest invoice: July 550 - 110 = 440.
        Assert.Equal(("0.00", "0.00", "1350.00", "0.00", "440.00", "1790.00"),
            (Get(row, "not_due"), Get(row, "days1_to30"), Get(row, "days31_to60"), Get(row, "days61_to90"), Get(row, "over90"), Get(row, "total")));
        Assert.Equal(s.Customer, row.GetProperty("customer_id").GetGuid());
        Assert.Equal("1790.00", Get(aging, "control_balance"));
        Assert.True(aging.GetProperty("matches_control").GetBoolean());
    }

    [Fact]
    public async Task Drafts_never_count_and_a_customer_statement_runs_from_the_opening_balance()
    {
        var s = await SeedAsync();
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-25", post = false,
            lines = new[] { new { account_id = s.Books.Sales, amount = "999.00" } },
        })).DataAsync(HttpStatusCode.Created);

        var statement = await GetAsync(s, $"customer-statement?customerId={s.Customer}&from=2026-10-01&to=2026-10-31");

        Assert.Equal("550.00", Get(statement, "opening_balance"));
        Assert.Equal([("invoice", "1350.00", "1900.00"), ("credit_note", "-110.00", "1790.00")],
            statement.GetProperty("items").EnumerateArray().Select(i => (Get(i, "type"), Get(i, "amount"), Get(i, "balance"))));
        Assert.Equal("1790.00", Get(statement, "closing_balance"));
    }

    /// <summary>July invoice 500 + VAT 50 (due 15 Jul); October invoice 1350 (due 29 Oct); credit note 110 on 20 Oct (due 3 Nov).</summary>
    private async Task<SalesInvoiceTests.Setup> SeedAsync()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-07-01",
            lines = new[] { new { account_id = s.Books.Sales, amount = "500.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(s.Url, SalesInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/credit-notes", new
        {
            customer_id = s.Customer, date = "2026-10-20",
            lines = new[] { new { account_id = s.Books.Sales, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);
        return s;
    }

    private static async Task<JsonElement> GetAsync(SalesInvoiceTests.Setup s, string report) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/{report}", Ct)).DataAsync();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
