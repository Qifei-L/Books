using System.Net;
using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;

namespace Books.Tests.Reporting;

/// <summary>Rule 43: AP aging by supplier and due date equals the Payable control balance; supplier statements.</summary>
public sealed class PayablesReportTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Aging_buckets_by_days_past_due_and_equals_the_payable_balance()
    {
        var s = await SeedAsync();

        var aging = await GetAsync(s, "ap-aging?asOf=2026-11-30");

        var row = Assert.Single(aging.GetProperty("rows").EnumerateArray());
        Assert.Equal(("0.00", "945.00", "0.00", "0.00", "550.00", "1495.00"),
            (Get(row, "not_due"), Get(row, "days1_to30"), Get(row, "days31_to60"), Get(row, "days61_to90"), Get(row, "over90"), Get(row, "total")));
        Assert.Equal("1495.00", Get(aging, "control_balance"));
        Assert.True(aging.GetProperty("matches_control").GetBoolean());
    }

    [Fact]
    public async Task Before_their_due_dates_invoices_are_not_due_and_drafts_never_count()
    {
        var s = await SeedAsync();
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            supplier_id = s.Supplier, date = "2026-10-25", post = false,
            lines = new[] { new { account_id = s.Rent, amount = "999.00" } },
        })).DataAsync(HttpStatusCode.Created);

        var aging = await GetAsync(s, "ap-aging?asOf=2026-10-25");

        var totals = aging.GetProperty("totals");
        // The July invoice was due 31 Jul: 86 days overdue on 25 Oct.
        Assert.Equal(("945.00", "550.00", "1495.00"), (Get(totals, "not_due"), Get(totals, "days61_to90"), Get(totals, "total")));
        Assert.True(aging.GetProperty("matches_control").GetBoolean());
    }

    [Fact]
    public async Task A_supplier_statement_shows_opening_items_and_running_balance()
    {
        var s = await SeedAsync();

        var statement = await GetAsync(s, $"supplier-statement?supplierId={s.Supplier}&from=2026-10-01&to=2026-10-31");

        Assert.Equal("550.00", Get(statement, "opening_balance"));
        var items = statement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([("invoice", "1055.00", "1605.00"), ("debit_note", "-110.00", "1495.00")],
            items.Select(i => (Get(i, "type"), Get(i, "amount"), Get(i, "balance"))));
        Assert.Equal("1495.00", Get(statement, "closing_balance"));
    }

    /// <summary>
    /// Old invoice 500 + VAT 50 dated 1 Jul (due 31 Jul); October invoice payable 1055 (due 14 Nov);
    /// debit note 110 dated 20 Oct (due 19 Nov).
    /// </summary>
    private async Task<PurchaseInvoiceTests.Setup> SeedAsync()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            supplier_id = s.Supplier, date = "2026-07-01",
            lines = new[] { new { account_id = s.Rent, amount = "500.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = true } },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(s.Url, PurchaseInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/debit-notes", new
        {
            supplier_id = s.Supplier, date = "2026-10-20",
            lines = new[] { new { account_id = s.Rent, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = true } },
        })).DataAsync(HttpStatusCode.Created);
        return s;
    }

    private static async Task<JsonElement> GetAsync(PurchaseInvoiceTests.Setup s, string report) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/{report}", Ct)).DataAsync();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
