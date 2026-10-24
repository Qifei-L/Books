using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Bank accounts in another currency (M12.2, owner decisions): payments, receipts and transfers are in the bank's
/// currency, settling a foreign-currency invoice at another rate realizes the exchange difference, and the bank
/// reconciles in its own currency.
/// </summary>
public sealed class ForeignBankTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_khr_receipt_settles_a_khr_invoice_and_the_rate_difference_is_realized()
    {
        var (s, khr) = await SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-01", currency = "KHR", exchange_rate = "4000",
            lines = new[] { new { account_id = s.Books.Sales, amount = "400000", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);                                                // 440,000 KHR = 110.00 USD

        var receipt = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
        {
            bank_account_id = khr, party_id = s.Customer, date = "2026-10-20", exchange_rate = "4100",
            lines = new[] { new { invoice_id = invoice.Id(), amount = "440000" } },
        })).DataAsync(HttpStatusCode.Created);

        var unnamed = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
        {
            bank_account_id = khr, party_id = s.Customer, date = "2026-10-21", lines = new[] { new { amount = "1000" } },
        });
        await unnamed.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal(("KHR", "440000", "110.00"), (Get(receipt, "currency"), Get(receipt, "total"), Get(receipt.GetProperty("lines")[0], "control_amount")));
        var lines = await JournalAsync(s, receipt);
        Assert.Equal(("107.32", "440000"), (lines[khr].Dr, lines[khr].Currency));             // 440,000 / 4,100
        Assert.Equal(("110.00", "2.68"), (lines[s.Account("1200")].Cr, lines[s.Account("7100")].Dr));
        var aging = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/ar-aging?asOf=2026-10-31", Ct)).DataAsync();
        Assert.Equal(("0.00", true), (Get(aging.GetProperty("totals"), "total"), aging.GetProperty("matches_control").GetBoolean()));
        var reconciliation = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/bank-accounts/{khr}/reconciliation?asOf=2026-10-31", Ct)).DataAsync();
        Assert.Equal(("KHR", "440000"), (Get(reconciliation, "currency"), Get(reconciliation, "book_balance")));
    }

    [Fact]
    public async Task A_usd_payment_settles_a_khr_bill_in_khr_and_the_gain_is_realized()
    {
        var (s, _) = await SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wing Printing", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        var bill = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01", currency = "KHR", exchange_rate = "4000",
            lines = new[] { new { account_id = s.Services, amount = "1000000" } },
        })).DataAsync(HttpStatusCode.Created);                                                // 250.00 USD

        var payment = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new
        {
            bank_account_id = bank, party_id = supplier, date = "2026-10-20",
            lines = new[] { new { invoice_id = bill.Id(), amount = "243.90", settled_amount = "1000000" } },
        })).DataAsync(HttpStatusCode.Created);

        var lines = await JournalAsync(s, payment);
        Assert.Equal(("250.00", "6.10", "243.90"), (lines[s.Account("2000")].Dr, lines[s.Account("7100")].Cr, lines[bank].Cr));
        Assert.Null(lines[bank].Currency);
        var aging = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/ap-aging?asOf=2026-10-31", Ct)).DataAsync();
        Assert.Equal(("0.00", true), (Get(aging.GetProperty("totals"), "total"), aging.GetProperty("matches_control").GetBoolean()));
    }

    [Fact]
    public async Task Transfers_between_currencies_carry_the_cost_and_realize_the_difference_coming_back()
    {
        var (s, khr) = await SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        var url = $"{s.LedgerUrl}/transfers";

        var missing = await s.Books.Admin.PostAsync(url, new { date = "2026-10-02", from_account_id = bank, to_account_id = khr, amount = "1000.00" });
        var buy = await (await s.Books.Admin.PostAsync(url, new { date = "2026-10-02", from_account_id = bank, to_account_id = khr, amount = "1000.00", to_amount = "4100000" }))
            .DataAsync(HttpStatusCode.Created);
        var sell = await (await s.Books.Admin.PostAsync(url, new
        {
            date = "2026-10-25", from_account_id = khr, to_account_id = bank, amount = "2050000", to_amount = "505.00", from_rate = "4100",
        })).DataAsync(HttpStatusCode.Created);

        await missing.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        var bought = await JournalAsync(s, buy);
        Assert.Equal(("1000.00", "4100000"), (bought[khr].Dr, bought[khr].Currency));
        var sold = await JournalAsync(s, sell);
        Assert.Equal(("500.00", "-2050000", "505.00", "5.00"), (sold[khr].Cr, sold[khr].Currency, sold[bank].Dr, sold[s.Account("7100")].Cr));
        var reconciliation = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/bank-accounts/{khr}/reconciliation?asOf=2026-10-31", Ct)).DataAsync();
        Assert.Equal("2050000", Get(reconciliation, "book_balance"));
    }

    [Fact]
    public async Task Journal_lines_on_a_khr_bank_need_the_khr_amount_and_a_used_account_stays_in_its_currency()
    {
        var (s, khr) = await SetUpAsync();
        object Line(Guid account, string? debit, string? credit, string? khrAmount = null) => new { account_id = account, debit, credit, currency_amount = khrAmount };

        var without = await s.Books.Admin.PostAsync(s.Books.JournalsUrl, new
        {
            accounting_date = "2026-10-05", post = true, lines = new[] { Line(khr, "100.00", null), Line(s.Books.Sales, null, "100.00") },
        });
        var with = await (await s.Books.Admin.PostAsync(s.Books.JournalsUrl, new
        {
            accounting_date = "2026-10-05", post = true, lines = new[] { Line(khr, "100.00", null, "410000"), Line(s.Books.Sales, null, "100.00") },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(s.Books.JournalsUrl, new
        {
            accounting_date = "2026-10-06", post = true, lines = new[] { Line(s.Books.Cash, "1.00", null), Line(s.Books.Sales, null, "1.00") },
        })).DataAsync(HttpStatusCode.Created);
        var used = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/bank-accounts", new { account_id = s.Books.Cash, bank_name = "Cash", currency = "KHR" });

        Assert.Contains("CURRENCY_AMOUNT_REQUIRED", await without.Content.ReadAsStringAsync(Ct));
        Assert.Equal("410000", Get(with.GetProperty("lines")[0], "currency_amount"));
        await used.AssertProblemAsync(HttpStatusCode.Conflict, "ACCOUNT_IN_USE");
    }

    /// <summary>The sales setup (USD ledger, Cambodia template, 4,100 KHR per USD) and a KHR bank account.</summary>
    private async Task<(SalesInvoiceTests.Setup, Guid Khr)> SetUpAsync()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var khr = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "1120", "Wing KHR", "assets")).Id();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/bank-accounts", new { account_id = khr, bank_name = "Wing", currency = "KHR" })).DataAsync(HttpStatusCode.Created);
        return (s, khr);
    }

    private static async Task<Dictionary<Guid, (string Dr, string Cr, string? Currency)>> JournalAsync(SalesInvoiceTests.Setup s, JsonElement document)
    {
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{document.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
        static decimal D(JsonElement l, string p) => decimal.Parse(Get(l, p), CultureInfo.InvariantCulture);
        return journal.GetProperty("lines").EnumerateArray().GroupBy(l => l.GetProperty("account_id").GetGuid()).ToDictionary(g => g.Key, g => (
            g.Sum(l => D(l, "debit")).ToString("F2", CultureInfo.InvariantCulture),
            g.Sum(l => D(l, "credit")).ToString("F2", CultureInfo.InvariantCulture),
            g.Select(l => l.GetProperty("currency_amount")).FirstOrDefault(c => c.ValueKind == JsonValueKind.String) is { ValueKind: JsonValueKind.String } c ? c.GetString() : null));
    }

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
