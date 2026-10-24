using System.Net;
using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;

namespace Books.Tests.Reporting;

/// <summary>Settlement by party (owner decision): named invoices first, then the oldest open invoices; the rest stays as the party's credit.</summary>
public sealed class SettlementTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Party_payments_settle_the_oldest_invoices_and_the_excess_stays_as_credit()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        var july = await InvoiceAsync(s, "2026-07-01", "500.00");           // 550 due 31 Jul
        await InvoiceAsync(s, "2026-08-01", "100.00");                       // 110 due 31 Aug
        await PayAsync(s, bank, "2026-09-10", new { invoice_id = july, amount = "200.00" }, new { amount = "300.00" });

        var september = await GetAsync(s, "ap-aging?asOf=2026-09-30");
        await PayAsync(s, bank, "2026-10-05", new { amount = "200.00" });
        var october = await GetAsync(s, "ap-aging?asOf=2026-10-31");
        var statement = await GetAsync(s, $"supplier-statement?supplierId={s.Supplier}&from=2026-09-01&to=2026-10-31");

        // 200 to July by name, 300 to the oldest open invoice (July): July 50 left (61-90 days), August 110 untouched (1-30 days).
        var sep = september.GetProperty("totals");
        Assert.Equal(("110.00", "50.00", "160.00"), (Get(sep, "days1_to30"), Get(sep, "days61_to90"), Get(sep, "total")));
        // 200 more settles July's 50 and August's 110; 40 stays as the supplier's credit.
        Assert.Equal(("-40.00", "-40.00", true), (Get(october.GetProperty("totals"), "not_due"), Get(october.GetProperty("totals"), "total"), october.GetProperty("matches_control").GetBoolean()));
        Assert.Equal([("payment", "-500.00"), ("payment", "-200.00")], statement.GetProperty("items").EnumerateArray().Select(i => (Get(i, "type"), Get(i, "amount"))));
        Assert.Equal(("660.00", "-40.00"), (Get(statement, "opening_balance"), Get(statement, "closing_balance")));
    }

    [Fact]
    public async Task A_line_on_the_payable_account_settles_and_a_supplier_refund_comes_back_as_a_receipt()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        var payable = s.Account("2000");
        var invoice = await InvoiceAsync(s, "2026-10-01", "100.00");                                  // 110
        await PayAsync(s, bank, "2026-10-05", new { account_id = payable, invoice_id = invoice, amount = "110.00" }, new { account_id = payable, amount = "40.00" });
        var overpaid = await GetAsync(s, "ap-aging?asOf=2026-10-31");

        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
        {
            bank_account_id = bank, party_id = s.Supplier, date = "2026-10-20", lines = new[] { new { account_id = payable, description = "Refund of overpayment", amount = "40.00" } },
        })).DataAsync(HttpStatusCode.Created);
        var refunded = await GetAsync(s, "ap-aging?asOf=2026-10-31");

        Assert.Equal(("-40.00", true), (Get(overpaid.GetProperty("totals"), "total"), overpaid.GetProperty("matches_control").GetBoolean()));
        Assert.Equal(("0.00", true), (Get(refunded.GetProperty("totals"), "total"), refunded.GetProperty("matches_control").GetBoolean()));
    }

    [Fact]
    public async Task An_invoice_a_payment_names_cannot_be_deleted()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        var invoice = await InvoiceAsync(s, "2026-10-01", "100.00");
        await PayAsync(s, bank, "2026-10-05", new { invoice_id = invoice, amount = "110.00" });

        var delete = await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{s.Url}/{invoice}", null, ifMatch: 1);

        await delete.AssertProblemAsync(HttpStatusCode.Conflict, "INVOICE_SETTLED");
    }

    private static async Task<Guid> InvoiceAsync(PurchaseInvoiceTests.Setup s, string date, string amount) =>
        (await (await s.Books.Admin.PostAsync(s.Url, new
        {
            supplier_id = s.Supplier, date, due_date = DateOnly.Parse(date).AddDays(30).ToString("yyyy-MM-dd"),
            lines = new[] { new { account_id = s.Rent, amount, tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = true } },
        })).DataAsync(HttpStatusCode.Created)).Id();

    private static async Task PayAsync(PurchaseInvoiceTests.Setup s, Guid bank, string date, params object[] lines) =>
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new { bank_account_id = bank, party_id = s.Supplier, date, lines })).DataAsync(HttpStatusCode.Created);

    private static async Task<JsonElement> GetAsync(PurchaseInvoiceTests.Setup s, string report) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/{report}", Ct)).DataAsync();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
