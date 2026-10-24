using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>Debit notes: the supplier's credit, posted as the opposite of an invoice, in their own number series.</summary>
public sealed class DebitNoteTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_debit_note_reverses_expense_vat_and_payable_for_the_supplier()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, PurchaseInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);

        var note = await (await s.Books.Admin.PostAsync(NotesUrl(s), new
        {
            supplier_id = s.Supplier,
            date = "2026-10-20",
            reference = "CN-77",
            original_invoice_id = invoice.Id(),
            lines = new[] { new { account_id = s.Rent, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = true } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("debit_note", 1L, "110.00"), (note.GetProperty("type").GetString(), note.GetProperty("invoice_no").GetInt64(), note.GetProperty("payable_total").GetString()));
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{note.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        Assert.Equal("purchase_debit_note", journal.GetProperty("source_kind").GetString());
        var lines = journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid());
        Assert.Equal("100.00", lines[s.Rent].GetProperty("credit").GetString());
        Assert.Equal("10.00", lines[s.Account("1300")].GetProperty("credit").GetString());
        Assert.Equal("110.00", lines[s.Account("2000")].GetProperty("debit").GetString());
        Assert.Equal(s.Supplier, lines[s.Account("2000")].GetProperty("party_id").GetGuid());
    }

    [Fact]
    public async Task Debit_notes_list_separately_and_must_match_the_original_supplier()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, PurchaseInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);
        var other = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Other supplier", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();

        var mismatched = await s.Books.Admin.PostAsync(NotesUrl(s), new
        {
            supplier_id = other, date = "2026-10-20", original_invoice_id = invoice.Id(),
            lines = new[] { new { account_id = s.Rent, amount = "10.00" } },
        });

        await mismatched.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(1, (await (await s.Books.Admin.GetAsync(s.Url, Ct)).DataAsync()).GetArrayLength());
        Assert.Equal(0, (await (await s.Books.Admin.GetAsync(NotesUrl(s), Ct)).DataAsync()).GetArrayLength());
    }

    private static string NotesUrl(PurchaseInvoiceTests.Setup s) => $"{s.LedgerUrl}/debit-notes";
}
