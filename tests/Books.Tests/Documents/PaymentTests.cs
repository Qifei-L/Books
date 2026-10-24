using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Payments and receipts (owner decision, Manager.io style): many lines per document; a line settles an invoice,
/// goes to the party's balance, or posts to an account with VAT and withholding; negative lines for fees, discounts
/// and tax a customer withheld.
/// </summary>
public sealed class PaymentTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_payment_settles_an_invoice_and_pays_the_bank_fee_on_another_line()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await BankAsync(s.Books);
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, PurchaseInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);

        var payment = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new
        {
            bank_account_id = bank, party_id = s.Supplier, date = "2026-10-20", reference = "TT-881",
            lines = new object[]
            {
                new { invoice_id = invoice.Id(), amount = "1055.00" },
                new { account_id = s.Supplies, description = "Bank transfer fee", amount = "2.00" },
            },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("payment", "1057.00", "posted"), (Get(payment, "type"), Get(payment, "total"), Get(payment, "status")));
        var lines = await JournalLinesAsync(s.Books, payment);
        Assert.Equal(("1055.00", s.Supplier), (lines[s.Account("2000")].Dr, lines[s.Account("2000")].Party));
        Assert.Equal("2.00", lines[s.Supplies].Dr);
        Assert.Equal("1057.00", lines[bank].Cr);
    }

    [Fact]
    public async Task Party_balance_and_direct_lines_post_vat_and_withholding()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await BankAsync(s.Books);

        var payment = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new
        {
            bank_account_id = bank, party_id = s.Supplier, date = "2026-10-20",
            lines = new object[]
            {
                new { description = "Advance for November", amount = "100.00" },
                new { account_id = s.Rent, amount = "1000.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = true, withholding_code_id = s.Taxes["WHT 10% rent"] },
            },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(["party", "account"], payment.GetProperty("lines").EnumerateArray().Select(l => Get(l, "kind")));
        Assert.Equal("1100.00", Get(payment, "total"));                    // 100 + (1000 + 100 VAT - 100 WHT)
        var lines = await JournalLinesAsync(s.Books, payment);
        Assert.Equal(("100.00", "1000.00", "100.00"), (lines[s.Account("2000")].Dr, lines[s.Rent].Dr, lines[s.Account("1300")].Dr));
        Assert.Equal(("100.00", "1100.00"), (lines[s.Account("2200")].Cr, lines[bank].Cr));
    }

    [Fact]
    public async Task A_receipt_records_tax_the_customer_withheld_as_a_negative_line()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var bank = await BankAsync(s.Books);
        var withheld = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "5950", "Tax withheld by customers", "cost_and_expense")).Id();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, SalesInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);

        var receipt = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
        {
            bank_account_id = bank, party_id = s.Customer, date = "2026-10-25",
            lines = new object[]
            {
                new { invoice_id = invoice.Id(), amount = "1350.00" },
                new { account_id = withheld, description = "WHT 15% withheld by customer", amount = "-202.50" },
            },
        })).DataAsync(HttpStatusCode.Created);

        Assert.Equal(("receipt", 1L, "1147.50"), (Get(receipt, "type"), receipt.GetProperty("payment_no").GetInt64(), Get(receipt, "total")));
        var lines = await JournalLinesAsync(s.Books, receipt);
        Assert.Equal(("1147.50", "202.50", "1350.00"), (lines[bank].Dr, lines[withheld].Dr, lines[s.Account("1200")].Cr));
    }

    [Fact]
    public async Task Wrong_references_and_shapes_are_refused()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await BankAsync(s.Books);
        var other = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Other supplier", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, PurchaseInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);
        var url = $"{s.LedgerUrl}/payments";

        var otherParty = await s.Books.Admin.PostAsync(url, new { bank_account_id = bank, party_id = other, date = "2026-10-20", lines = new[] { new { invoice_id = invoice.Id(), amount = "10.00" } } });
        var control = await s.Books.Admin.PostAsync(url, new { bank_account_id = bank, date = "2026-10-20", lines = new[] { new { account_id = s.Account("2000"), amount = "10.00" } } });
        var noParty = await s.Books.Admin.PostAsync(url, new { bank_account_id = bank, date = "2026-10-20", lines = new[] { new { amount = "10.00" } } });
        var notBank = await s.Books.Admin.PostAsync(url, new { bank_account_id = s.Rent, date = "2026-10-20", lines = new[] { new { account_id = s.Supplies, amount = "10.00" } } });
        var negative = await s.Books.Admin.PostAsync(url, new { bank_account_id = bank, date = "2026-10-20", lines = new[] { new { account_id = s.Supplies, amount = "-10.00" } } });
        var receiptWht = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
        {
            bank_account_id = bank, date = "2026-10-20", lines = new[] { new { account_id = s.Books.Sales, amount = "10.00", withholding_code_id = s.Taxes["WHT 10% rent"] } },
        });

        foreach (var response in new[] { otherParty, control, noParty, notBank, negative, receiptWht })
        {
            await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        }
    }

    [Fact]
    public async Task Deleting_a_posted_payment_removes_its_posting()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await BankAsync(s.Books);
        var payment = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new
        {
            bank_account_id = bank, date = "2026-10-20", lines = new[] { new { account_id = s.Supplies, amount = "30.00" } },
        })).DataAsync(HttpStatusCode.Created);

        await (await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{s.LedgerUrl}/payments/{payment.Id()}", null, ifMatch: 1)).DataAsync();
        var gl = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/gl-detail?accountId={bank}&from=2026-01-01&to=2026-12-31", Ct)).DataAsync();

        Assert.Equal("0.00", Get(gl, "closing_balance"));
    }

    /// <summary>Gives the books' Cash account bank details, which adds the Bank usage.</summary>
    internal static async Task<Guid> BankAsync(Journal.JournalBooks books)
    {
        await (await books.Admin.PostAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/bank-accounts", new { account_id = books.Cash, bank_name = "ABA" }))
            .DataAsync(HttpStatusCode.Created);
        return books.Cash;
    }

    private static async Task<Dictionary<Guid, (string Dr, string Cr, Guid? Party)>> JournalLinesAsync(Journal.JournalBooks books, JsonElement document)
    {
        var journal = await (await books.Admin.GetAsync($"{books.JournalsUrl}/{document.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        return journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid(),
            l => (Get(l, "debit"), Get(l, "credit"), l.GetProperty("party_id").ValueKind == JsonValueKind.Null ? (Guid?)null : l.GetProperty("party_id").GetGuid()));
    }

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
