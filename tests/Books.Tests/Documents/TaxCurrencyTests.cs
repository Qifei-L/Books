using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Documents;

/// <summary>
/// The tax currency slot (owner decision): the Cambodia pack shows the gross total and VAT in KHR at the rate on
/// or before the document date, kept as a snapshot; a missing rate keeps the document a draft; a rate entered on
/// the document needs a reason.
/// </summary>
public sealed class TaxCurrencyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invoice_shows_khr_totals_at_the_latest_rate_on_or_before_its_date()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        await RateAsync(s, "2026-10-14", "4100");
        await RateAsync(s, "2026-10-16", "4110");

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, PurchaseInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);

        var khr = invoice.GetProperty("tax_currency");
        Assert.Equal(("KHR", "4100", "2026-10-14", "NBC", "4735500", "430500"),
            (Get(khr, "currency"), Get(khr, "rate"), Get(khr, "rate_date"), Get(khr, "rate_source"), Get(khr, "gross_total"), Get(khr, "tax_total")));
    }

    [Fact]
    public async Task A_later_rate_change_leaves_the_snapshot_until_the_invoice_is_saved_again()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        await RateAsync(s, "2026-10-15", "4100");
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, PurchaseInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);

        await RateAsync(s, "2026-10-15", "4200");
        var unchanged = await GetAsync(s, invoice.Id());
        var saved = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.Url}/{invoice.Id()}", PurchaseInvoiceTests.Body(s), ifMatch: 1)).DataAsync();

        Assert.Equal("4100", Get(unchanged.GetProperty("tax_currency"), "rate"));
        Assert.Equal("4200", Get(saved.GetProperty("tax_currency"), "rate"));
    }

    [Fact]
    public async Task Without_a_rate_the_invoice_is_a_draft_until_a_rate_is_entered_with_a_reason()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var body = PurchaseInvoiceTests.Body(s, date: "2025-12-31"); // before the setup's first rate

        var draft = await (await s.Books.Admin.PostAsync(s.Url, body)).DataAsync(HttpStatusCode.UnprocessableEntity);
        var noReason = await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.Url}/{draft.Id()}", With(body, rate: "4080"), ifMatch: 1);
        var posted = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.Url}/{draft.Id()}", With(body, rate: "4080", reason: "Rate printed on the supplier invoice"), ifMatch: 1)).DataAsync();

        Assert.Equal(("draft", "TAX_RATE_MISSING"), (Get(draft, "status"), Get(draft.GetProperty("issues")[0], "code")));
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("tax_currency").ValueKind);
        await noReason.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal("posted", Get(posted, "status"));
        var khr = posted.GetProperty("tax_currency");
        Assert.Equal(("4080", "Override", "Rate printed on the supplier invoice", "2025-12-31"),
            (Get(khr, "rate"), Get(khr, "rate_source"), Get(khr, "reason"), Get(khr, "rate_date")));
    }

    [Fact]
    public async Task Ledgers_without_the_cambodia_pack_have_no_tax_currency()
    {
        var books = await JournalBooks.CreateAsync(host);
        var ledgerUrl = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";
        var payable = await Books.Tests.Ledger.AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "2000", "Payables", "liabilities");
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"{ledgerUrl}/accounts/{payable.Id()}/usages", new { usages = new[] { "payable" } }, ifMatch: 1)).DataAsync();
        var supplier = (await (await books.Admin.PostAsync($"{ledgerUrl}/parties", new { name = "Supplier", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();

        var invoice = await (await books.Admin.PostAsync($"{ledgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-15", lines = new[] { new { account_id = books.Sales, amount = "10.00" } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(JsonValueKind.Null, invoice.GetProperty("tax_currency").ValueKind);
    }

    private static object With(object body, string? rate = null, string? reason = null)
    {
        var json = JsonSerializer.SerializeToNode(body)!.AsObject();
        json["tax_currency_rate"] = rate;
        json["tax_currency_reason"] = reason;
        return json;
    }

    private static async Task RateAsync(PurchaseInvoiceTests.Setup s, string date, string rate) =>
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{s.Books.OrgId}/exchange-rates",
            new { date, from_currency = "USD", to_currency = "KHR", rate, source = "NBC" })).DataAsync();

    private static async Task<JsonElement> GetAsync(PurchaseInvoiceTests.Setup s, Guid id) =>
        (await (await s.Books.Admin.GetAsync(s.Url, Ct)).DataAsync()).EnumerateArray().Single(i => i.Id() == id);

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
