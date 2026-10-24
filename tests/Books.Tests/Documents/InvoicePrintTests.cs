using System.Net;
using System.Text;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>
/// Owner decisions for M9: invoices print as bilingual PDFs in three styles chosen from the lines' tax codes,
/// numbers carry the ledger's prefix, and only administrators change what the seller block shows.
/// </summary>
public sealed class InvoicePrintTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invoice_prints_as_a_tax_invoice_pdf_with_the_ledgers_prefix()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var settings = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/invoice-settings", new
        {
            name_khmer = "ក្រុមហ៊ុន អង្គរ ឯ.ក", name_english = "Angkor Trading Co., Ltd.", tax_id = "K001-123456789",
            address_english = "Phnom Penh", invoice_prefix = "AT-", credit_note_prefix = "ATC-",
        })).DataAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, SalesInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);

        var response = await s.Books.Admin.GetAsync($"{s.Url}/{invoice.Id()}/pdf", Ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("tax", response.Headers.GetValues("Invoice-Style").Single());
        Assert.Equal("AT-00001.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(("AT-", "1"), (settings.GetProperty("invoice_prefix").GetString(), settings.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task The_style_follows_the_tax_codes_and_can_be_chosen()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        async Task<string> Style(object lines, string query = "")
        {
            var invoice = await (await s.Books.Admin.PostAsync(s.Url, new { customer_id = s.Customer, date = "2026-10-15", lines })).DataAsync(HttpStatusCode.Created);
            var response = await s.Books.Admin.GetAsync($"{s.Url}/{invoice.Id()}/pdf{query}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return response.Headers.GetValues("Invoice-Style").Single();
        }

        var zeroRated = await Style(new[] { new { account_id = s.Services, amount = "250.00", tax_code_id = s.Taxes["VAT 0% (zero-rated)"] } });
        var noCodes = await Style(new[] { new { account_id = s.Books.Sales, amount = "100.00" } });
        var chosen = await Style(new[] { new { account_id = s.Books.Sales, amount = "100.00" } }, "?style=tax&stateCharge=true");
        var unknown = await s.Books.Admin.GetAsync($"{s.Url}/{Guid.NewGuid()}/pdf?style=fancy", Ct);

        Assert.Equal(("zero_non_vat", "commercial", "tax"), (zeroRated, noCodes, chosen));
        await unknown.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_credit_note_prints_from_its_own_series_and_not_as_an_invoice()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, SalesInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);
        var note = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/credit-notes", new
        {
            customer_id = s.Customer, date = "2026-10-20", original_invoice_id = invoice.Id(),
            lines = new[] { new { account_id = s.Books.Sales, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);

        var response = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/credit-notes/{note.Id()}/pdf", Ct);
        var asInvoice = await s.Books.Admin.GetAsync($"{s.Url}/{note.Id()}/pdf", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CN-00001.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(HttpStatusCode.NotFound, asInvoice.StatusCode);
    }

    [Fact]
    public async Task Only_administrators_change_invoice_settings()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        using var editor = await s.Books.AddMemberAsync("edit");
        var url = $"{s.LedgerUrl}/invoice-settings";

        var defaults = await (await editor.GetAsync(url, Ct)).DataAsync();
        var byEditor = await editor.WriteAsync(HttpMethod.Put, url, new { invoice_prefix = "X-" });
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, url, new { invoice_prefix = "X-" })).DataAsync();
        var stale = await s.Books.Admin.WriteAsync(HttpMethod.Put, url, new { invoice_prefix = "Y-" }, ifMatch: 7);
        var tooLong = await s.Books.Admin.WriteAsync(HttpMethod.Put, url, new { invoice_prefix = new string('X', 21) }, ifMatch: 1);

        Assert.Equal(("INV-", "CN-"), (defaults.GetProperty("invoice_prefix").GetString(), defaults.GetProperty("credit_note_prefix").GetString()));
        await byEditor.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        await tooLong.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }
}
