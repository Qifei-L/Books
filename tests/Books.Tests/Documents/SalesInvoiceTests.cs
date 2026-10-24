using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Rules 31, 32, 34 and owner decisions for sales: invoices post income, output VAT and AR naming the customer,
/// credit notes post the opposite, KHR totals come from the tax pack, failed postings stay drafts with reasons.
/// </summary>
public sealed class SalesInvoiceTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invoice_posts_income_output_vat_and_receivable_to_the_customer()
    {
        var s = await SetUpAsync();

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, Body(s))).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("1250.00", "100.00", "1350.00", "2026-10-29"),
            (Get(invoice, "net_total"), Get(invoice, "tax_total"), Get(invoice, "gross_total"), Get(invoice, "due_date"))); // 15 Oct + 14 days
        Assert.Equal(("5535000", "410000"), (Get(invoice.GetProperty("tax_currency"), "gross_total"), Get(invoice.GetProperty("tax_currency"), "tax_total")));
        var lines = await JournalLinesAsync(s, invoice);
        Assert.Equal("1000.00", lines[s.Books.Sales].Cr);
        Assert.Equal("250.00", lines[s.Services].Cr);                 // zero-rated export service
        Assert.Equal("100.00", lines[s.Account("2100")].Cr);          // output VAT
        Assert.Equal("1350.00", lines[s.Account("1200")].Dr);         // receivable: gross
        Assert.Equal(s.Customer, lines[s.Account("1200")].Party.GetGuid());
    }

    [Fact]
    public async Task Accommodation_tax_is_charged_on_top_and_is_part_of_the_vat_base()
    {
        var s = await SetUpAsync();

        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new[] { new { account_id = s.Services, description = "Room nights", amount = "1000.00", tax_code_id = s.Taxes["VAT 10% (sales)"], other_tax_code_id = s.Taxes["Accommodation tax 2%"] } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("1000.00", "20.00", "102.00", "1122.00"), (Get(invoice, "net_total"), Get(invoice, "other_tax_total"), Get(invoice, "tax_total"), Get(invoice, "gross_total")));
        var lines = await JournalLinesAsync(s, invoice);
        Assert.Equal(("20.00", "102.00", "1122.00"), (lines[s.Account("2400")].Cr, lines[s.Account("2100")].Cr, lines[s.Account("1200")].Dr));
        var boxes = TaxReturnTests.Boxes(await TaxReturnTests.GetAsync(s, "tax-returns/2026-10"));
        Assert.Equal(("1000.00", "20.00", "1020.00", "102.00"),
            (boxes["t701_accommodation_base"].Usd, boxes["t701_accommodation"].Usd, boxes["t701_14"].Usd, boxes["t701_15"].Usd));
    }

    [Fact]
    public async Task A_credit_note_posts_the_opposite_in_its_own_series()
    {
        var s = await SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, Body(s))).DataAsync(HttpStatusCode.Created);

        var note = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/credit-notes", new
        {
            customer_id = s.Customer, date = "2026-10-20", original_invoice_id = invoice.Id(),
            lines = new[] { new { account_id = s.Books.Sales, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("credit_note", 1L, "110.00"), (Get(note, "type"), note.GetProperty("invoice_no").GetInt64(), Get(note, "gross_total")));
        var lines = await JournalLinesAsync(s, note);
        Assert.Equal(("100.00", "10.00", "110.00"), (lines[s.Books.Sales].Dr, lines[s.Account("2100")].Dr, lines[s.Account("1200")].Cr));
        var invoices = await (await s.Books.Admin.GetAsync(s.Url, Ct)).DataAsync();
        Assert.Single(invoices.EnumerateArray());
    }

    [Fact]
    public async Task A_party_that_is_not_a_customer_keeps_the_invoice_a_draft()
    {
        var s = await SetUpAsync();
        var supplier = await Party(s.Books, new { name = "Only a supplier", is_supplier = true });

        var draft = await (await s.Books.Admin.PostAsync(s.Url, Body(s, customer: supplier))).DataAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("draft", Get(draft, "status"));
        Assert.Contains(draft.GetProperty("issues").EnumerateArray(), i => Get(i, "code") == "NOT_AN_ACTIVE_CUSTOMER");
    }

    [Fact]
    public async Task Purchase_vat_codes_and_unknown_accounts_are_refused()
    {
        var s = await SetUpAsync();

        var inputVat = await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-15",
            lines = new[] { new { account_id = s.Books.Sales, amount = "10.00", tax_code_id = s.Taxes["VAT 10% (purchases)"] } },
        });
        var unknown = await s.Books.Admin.PostAsync(s.Url, new { customer_id = s.Customer, date = "2026-10-15", lines = new[] { new { account_id = Guid.NewGuid(), amount = "10.00" } } });

        await inputVat.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await unknown.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task The_journal_changes_only_through_the_invoice_and_the_receivable_usage_stays()
    {
        var s = await SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, Body(s))).DataAsync(HttpStatusCode.Created);
        var journalUrl = $"{s.Books.JournalsUrl}/{invoice.GetProperty("journal_id").GetGuid()}";

        var directDelete = await s.Books.Admin.WriteAsync(HttpMethod.Delete, journalUrl, null, ifMatch: 1);
        var removeUsage = await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/accounts/{s.Account("1200")}/usages",
            new { usages = Array.Empty<string>() }, ifMatch: await AccountVersionAsync(s, "1200"));
        var deleted = await (await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{s.Url}/{invoice.Id()}", null, ifMatch: 1)).DataAsync();

        await directDelete.AssertProblemAsync(HttpStatusCode.Conflict, "JOURNAL_FROM_DOCUMENT");
        await removeUsage.AssertProblemAsync(HttpStatusCode.Conflict, "USAGE_IN_USE");
        Assert.True(deleted.GetProperty("deleted").GetBoolean());
    }

    internal sealed record Setup(JournalBooks Books, Guid Customer, Guid Services, Dictionary<string, Guid> Codes, Dictionary<string, Guid> Taxes)
    {
        public string LedgerUrl => $"/api/v1/organizations/{Books.OrgId}/ledgers/{Books.LedgerId}";
        public string Url => $"{LedgerUrl}/sales-invoices";
        public Guid Account(string code) => Codes[code];
    }

    internal async Task<Setup> SetUpAsync(string? subject = null)
    {
        var books = await JournalBooks.CreateAsync(host, subject: subject);
        var ledgerUrl = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";
        await (await books.Admin.PostAsync($"{ledgerUrl}/templates/cambodia-2026", new { })).DataAsync();
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{books.OrgId}/exchange-rates",
            new { date = "2026-01-01", from_currency = "USD", to_currency = "KHR", rate = "4100", source = "NBC" })).DataAsync();
        var services = (await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "4100", "Export services", "income")).Id();
        var customer = await Party(books, new { name = "Angkor Hotels Co., Ltd.", is_customer = true, payment_terms_days = 14 });
        var codes = (await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray().ToDictionary(a => Get(a, "code"), a => a.Id());
        var taxes = (await (await books.Admin.GetAsync($"{ledgerUrl}/tax-codes", Ct)).DataAsync()).EnumerateArray().ToDictionary(t => Get(t, "name"), t => t.Id());
        return new Setup(books, customer, services, codes, taxes);
    }

    /// <summary>Goods 1000 with VAT 10%; an export service of 250 at VAT 0%.</summary>
    internal static object Body(Setup s, string date = "2026-10-15", Guid? customer = null) => new
    {
        customer_id = customer ?? s.Customer,
        date,
        reference = "INV-2026-0042",
        lines = new object[]
        {
            new { account_id = s.Books.Sales, description = "Goods", quantity = "4", unit_price = "250.00", tax_code_id = s.Taxes["VAT 10% (sales)"] },
            new { account_id = s.Services, description = "Consulting (export)", amount = "250.00", tax_code_id = s.Taxes["VAT 0% (zero-rated)"] },
        },
    };

    private static async Task<Dictionary<Guid, (string Dr, string Cr, JsonElement Party)>> JournalLinesAsync(Setup s, JsonElement document)
    {
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{document.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        return journal.GetProperty("lines").EnumerateArray()
            .ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => (Get(l, "debit"), Get(l, "credit"), l.GetProperty("party_id")));
    }

    private static async Task<long> AccountVersionAsync(Setup s, string code) =>
        long.Parse((await AccountTests.ListAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId)).EnumerateArray()
            .Single(a => Get(a, "code") == code).GetProperty("version").GetString()!);

    private static async Task<Guid> Party(JournalBooks books, object body) =>
        (await (await books.Admin.PostAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/parties", body)).DataAsync(HttpStatusCode.Created)).Id();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
