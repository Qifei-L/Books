using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Rules 18, 31, 32, 34 and owner decisions: purchase invoices post VAT (deductible or not), withholding per line,
/// and AP naming the supplier; failed postings stay drafts with reasons.
/// </summary>
public sealed class PurchaseInvoiceTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invoice_posts_net_vat_withholding_and_payable_to_the_supplier()
    {
        var s = await SetUpAsync();

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, Body(s))).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("1050.00", "105.00", "1155.00", "100.00", "1055.00"),
            (Get(invoice, "net_total"), Get(invoice, "tax_total"), Get(invoice, "gross_total"), Get(invoice, "withholding_total"), Get(invoice, "payable_total")));
        Assert.Equal("2026-11-14", Get(invoice, "due_date")); // 15 Oct + 30 days of supplier terms
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{invoice.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        var lines = journal.GetProperty("lines").EnumerateArray()
            .ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => (Dr: Get(l, "debit"), Cr: Get(l, "credit"), Party: l.GetProperty("party_id")));
        Assert.Equal("1000.00", lines[s.Rent].Dr);
        Assert.Equal("100.00", lines[s.Account("1300")].Dr);          // deductible VAT (valid tax invoice)
        Assert.Equal("5.00", lines[s.Account("5900")].Dr);            // non-deductible VAT (no valid tax invoice)
        Assert.Equal("100.00", lines[s.Account("2200")].Cr);          // withholding 10% of rent, VAT excluded
        Assert.Equal("1055.00", lines[s.Account("2000")].Cr);         // payable: gross less withholding
        Assert.Equal(s.Supplier, lines[s.Account("2000")].Party.GetGuid());
    }

    [Fact]
    public async Task A_tax_override_keeps_the_calculated_amount_and_the_reason()
    {
        var s = await SetUpAsync();
        var body = Body(s, rentTax: ("99.98", "Supplier invoice shows 99.98"));

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, body)).DataAsync(HttpStatusCode.Created);

        var rentLine = invoice.GetProperty("lines")[0];
        Assert.Equal(("100.00", "99.98", true, "Supplier invoice shows 99.98"),
            (Get(rentLine, "tax_calculated"), Get(rentLine, "tax_amount"), rentLine.GetProperty("tax_adjusted").GetBoolean(), Get(rentLine, "tax_reason")));
    }

    [Fact]
    public async Task An_invoice_that_cannot_post_is_a_draft_with_reasons_until_fixed()
    {
        var s = await SetUpAsync();
        await new JournalEditTests(host).CloseThroughAsync(s.Books, "2026-10-31");

        var draft = await (await s.Books.Admin.PostAsync(s.Url, Body(s))).DataAsync(HttpStatusCode.UnprocessableEntity);
        var posted = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.Url}/{draft.Id()}", Body(s, date: "2026-11-02"), ifMatch: 1)).DataAsync();

        Assert.Equal(("draft", "DATE_CLOSED"), (Get(draft, "status"), Get(draft.GetProperty("issues")[0], "code")));
        Assert.Equal("posted", Get(posted, "status"));
    }

    [Fact]
    public async Task A_party_that_is_not_a_supplier_keeps_the_invoice_a_draft()
    {
        var s = await SetUpAsync();
        var customer = await Party(s.Books, new { name = "Only a customer", is_customer = true });

        var draft = await (await s.Books.Admin.PostAsync(s.Url, Body(s, supplier: customer))).DataAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Contains(draft.GetProperty("issues").EnumerateArray(), i => Get(i, "code") == "NOT_AN_ACTIVE_SUPPLIER");
    }

    [Fact]
    public async Task Sales_vat_codes_and_ledgers_without_payables_are_refused()
    {
        var s = await SetUpAsync();
        var salesVat = (await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-codes", Ct)).DataAsync()).EnumerateArray()
            .Single(t => Get(t, "name") == "VAT 10% (sales)").Id();
        var bare = await JournalBooks.CreateAsync(host);
        var bareSupplier = await Party(bare, new { name = "Supplier", is_supplier = true });

        var wrongCode = await s.Books.Admin.PostAsync(s.Url, Body(s, rentTaxCode: salesVat));
        var noPayable = await bare.Admin.PostAsync($"/api/v1/organizations/{bare.OrgId}/ledgers/{bare.LedgerId}/purchase-invoices", new
        {
            supplier_id = bareSupplier, date = "2026-10-15", lines = new[] { new { account_id = bare.Cash, amount = "1.00" } },
        });

        await wrongCode.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await noPayable.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task The_payable_usage_stays_while_posted_invoices_rely_on_it()
    {
        var s = await SetUpAsync();
        await (await s.Books.Admin.PostAsync(s.Url, Body(s))).DataAsync(HttpStatusCode.Created);
        var payable = (await AccountTests.ListAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId)).EnumerateArray().Single(a => a.Id() == s.Account("2000"));

        var response = await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/accounts/{s.Account("2000")}/usages",
            new { usages = Array.Empty<string>() }, ifMatch: long.Parse(Get(payable, "version")));

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "USAGE_IN_USE");
    }

    internal sealed record Setup(JournalBooks Books, Guid Supplier, Guid Rent, Guid Supplies, Dictionary<string, Guid> Codes, Dictionary<string, Guid> Taxes)
    {
        public string LedgerUrl => $"/api/v1/organizations/{Books.OrgId}/ledgers/{Books.LedgerId}";
        public string Url => $"{LedgerUrl}/purchase-invoices";
        public Guid Account(string code) => Codes[code];
    }

    internal async Task<Setup> SetUpAsync(string? subject = null)
    {
        var books = await JournalBooks.CreateAsync(host, subject: subject);
        var ledgerUrl = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";
        await (await books.Admin.PostAsync($"{ledgerUrl}/templates/cambodia-2026", new { })).DataAsync();
        var rent = (await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "5000", "Rent", "cost_and_expense")).Id();
        var supplies = (await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "5100", "Office supplies", "cost_and_expense")).Id();
        var supplier = await Party(books, new { name = "Phnom Penh Property Ltd.", is_supplier = true, payment_terms_days = 30 });
        var codes = (await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray().ToDictionary(a => Get(a, "code"), a => a.Id());
        var taxes = (await (await books.Admin.GetAsync($"{ledgerUrl}/tax-codes", Ct)).DataAsync()).EnumerateArray().ToDictionary(t => Get(t, "name"), t => t.Id());
        return new Setup(books, supplier, rent, supplies, codes, taxes);
    }

    /// <summary>Rent 1000 with VAT 10% (valid invoice) and WHT 10%; supplies 50 with VAT 10% but no valid tax invoice.</summary>
    internal static object Body(Setup s, string date = "2026-10-15", (string Amount, string Reason)? rentTax = null, Guid? rentTaxCode = null, Guid? supplier = null) => new
    {
        supplier_id = supplier ?? s.Supplier,
        date,
        reference = "PPP-2026-118",
        lines = new object[]
        {
            new { account_id = s.Rent, description = "October rent", amount = "1000.00", tax_code_id = rentTaxCode ?? s.Taxes["VAT 10% (purchases)"],
                tax_amount = rentTax?.Amount, tax_reason = rentTax?.Reason, valid_tax_invoice = true, withholding_code_id = s.Taxes["WHT 10% rent"] },
            new { account_id = s.Supplies, description = "Paper", quantity = "10", unit_price = "5.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = false },
        },
    };

    private static async Task<Guid> Party(JournalBooks books, object body) =>
        (await (await books.Admin.PostAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/parties", body)).DataAsync(HttpStatusCode.Created)).Id();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
