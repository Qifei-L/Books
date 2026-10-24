using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Year-end revaluation (M12.2b, owner decision: yearly, unrealized kept apart): KHR bank balances and open KHR
/// invoices are restated at the closing rate, the difference is unrealized, and the posting reverses the next day.
/// </summary>
public sealed class FxRevaluationTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Khr_bank_balances_and_open_khr_invoices_are_restated_at_the_closing_rate_and_reversed_the_next_day()
    {
        var (s, khr, bank) = await SetUpAsync();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Wing Printing", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-01", currency = "KHR", exchange_rate = "4000",
            lines = new[] { new { account_id = s.Books.Sales, amount = "400000", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);                                                   // 440,000 KHR = 110.00
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01", currency = "KHR", exchange_rate = "4000", lines = new[] { new { account_id = s.Services, amount = "1000000" } },
        })).DataAsync(HttpStatusCode.Created);                                                   // 1,000,000 KHR = 250.00
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/transfers", new
        {
            date = "2026-10-02", from_account_id = bank, to_account_id = khr, amount = "1000.00", to_amount = "4100000",
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
        {
            bank_account_id = khr, party_id = s.Customer, date = "2026-10-20", exchange_rate = "4100", lines = new[] { new { invoice_id = invoice.Id(), amount = "220000" } },
        })).DataAsync(HttpStatusCode.Created);                                                   // half: 55.00 left open, 220,000 KHR
        var url = $"{s.LedgerUrl}/fx-revaluations";

        var preview = await (await s.Books.Admin.GetAsync($"{url}/preview?date=2026-12-31&khrRate=4400", Ct)).DataAsync();
        var posted = await (await s.Books.Admin.PostAsync(url, new { date = "2026-12-31", rates = new Dictionary<string, string> { ["KHR"] = "4400" } }))
            .DataAsync(HttpStatusCode.Created);
        var again = await s.Books.Admin.PostAsync(url, new { date = "2026-12-31", rates = new Dictionary<string, string> { ["KHR"] = "4400" } });

        // Bank: 4,320,000 KHR was 1,053.66, now 981.82. Receivable: 220,000 KHR was 55.00, now 50.00. Payable: 1,000,000 KHR was 250.00, now 227.27.
        Assert.Equal(["bank -71.84", "receivable -5.00", "payable 22.73"], preview.GetProperty("lines").EnumerateArray().Select(l => $"{Get(l, "kind")} {Get(l, "adjustment")}"));
        Assert.Equal(("-54.11", "4400"), (Get(preview, "total"), Get(preview.GetProperty("rates"), "KHR")));
        var journal = await JournalAsync(s, posted.GetProperty("journal_id").GetGuid());
        Assert.Equal(("2026-12-31", "71.84", "0"), (Get(journal.Journal, "accounting_date"), journal.Lines[khr].Cr, journal.Lines[khr].Currency));
        Assert.Equal(("5.00", "22.73", "54.11"), (journal.Lines[s.Account("1290")].Cr, journal.Lines[s.Account("2090")].Dr, journal.Lines[s.Account("7110")].Dr));
        var reversal = await JournalAsync(s, posted.GetProperty("reversal_journal_id").GetGuid());
        Assert.Equal(("2027-01-01", "54.11"), (Get(reversal.Journal, "accounting_date"), reversal.Lines[s.Account("7110")].Cr));
        await again.AssertProblemAsync(HttpStatusCode.Conflict, "REVALUATION_EXISTS");
        var aging = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/ar-aging?asOf=2026-12-31", Ct)).DataAsync();
        Assert.True(aging.GetProperty("matches_control").GetBoolean());
        var reconciliation = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/bank-accounts/{khr}/reconciliation?asOf=2027-01-31", Ct)).DataAsync();
        Assert.Equal(("4320000", 2), (Get(reconciliation, "book_balance"), reconciliation.GetProperty("pending").GetArrayLength()));
    }

    [Fact]
    public async Task Deleting_a_revaluation_removes_both_postings_and_nothing_foreign_means_nothing_to_post()
    {
        var (s, khr, bank) = await SetUpAsync();
        var url = $"{s.LedgerUrl}/fx-revaluations";
        var nothing = await s.Books.Admin.PostAsync(url, new { date = "2026-12-31" });
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/transfers", new
        {
            date = "2026-10-02", from_account_id = bank, to_account_id = khr, amount = "1000.00", to_amount = "4000000",
        })).DataAsync(HttpStatusCode.Created);

        var posted = await (await s.Books.Admin.PostAsync(url, new { date = "2026-12-31" })).DataAsync(HttpStatusCode.Created);  // table rate 4,100
        var deleted = await (await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{url}/{posted.Id()}", null, ifMatch: 1)).DataAsync();
        var redo = await s.Books.Admin.PostAsync(url, new { date = "2026-12-31" });

        await nothing.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal("-24.39", Get(posted, "total"));                                            // 4,000,000 / 4,100 = 975.61
        Assert.True(deleted.GetProperty("deleted").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, redo.StatusCode);
        var list = await (await s.Books.Admin.GetAsync(url, Ct)).DataAsync();
        Assert.Equal(["posted", "deleted"], list.EnumerateArray().Select(r => Get(r, "status")));
    }

    [Fact]
    public async Task A_khr_overpayment_stays_in_khr_and_is_revalued_while_the_balance_sheet_shows_one_receivables_line()
    {
        var (s, khr, _) = await SetUpAsync();
        var invoice = await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-01", currency = "KHR", exchange_rate = "4000",
            lines = new[] { new { account_id = s.Books.Sales, amount = "400000", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);                                                   // 440,000 KHR = 110.00
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/receipts", new
        {
            bank_account_id = khr, party_id = s.Customer, date = "2026-10-20", exchange_rate = "4100", lines = new[] { new { invoice_id = invoice.Id(), amount = "500000" } },
        })).DataAsync(HttpStatusCode.Created);                                                   // 60,000 KHR overpaid: a 15.00 credit at 4,000

        var preview = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/fx-revaluations/preview?date=2026-12-31&khrRate=4400", Ct)).DataAsync();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/fx-revaluations", new { date = "2026-12-31", rates = new Dictionary<string, string> { ["KHR"] = "4400" } }))
            .DataAsync(HttpStatusCode.Created);
        var sheet = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/balance-sheet?asOf=2026-12-31", Ct)).DataAsync();

        var credit = preview.GetProperty("lines").EnumerateArray().Single(l => Get(l, "kind") == "receivable");
        Assert.Equal(("Credit balance in KHR", "-60000", "-15.00", "-13.64", "1.36"),
            (Get(credit, "description"), Get(credit, "foreign_amount"), Get(credit, "before"), Get(credit, "after"), Get(credit, "adjustment")));
        var accounts = Flatten(sheet.GetProperty("sections")[0].GetProperty("lines")).ToList();
        Assert.DoesNotContain(accounts, l => l.Code == "1290");
        Assert.Equal("-13.64", accounts.Single(l => l.Code == "1200").Amount);                  // -15.00 control + 1.36 revaluation
        Assert.True(sheet.GetProperty("balanced").GetBoolean());
    }

    [Fact]
    public async Task Closing_the_year_with_khr_balances_not_revalued_warns_but_closes()
    {
        var (s, khr, bank) = await SetUpAsync();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/transfers", new
        {
            date = "2026-10-02", from_account_id = bank, to_account_id = khr, amount = "1000.00", to_amount = "4000000",
        })).DataAsync(HttpStatusCode.Created);
        var closeUrl = $"/api/v1/organizations/{s.Books.OrgId}/ledgers/{s.Books.LedgerId}/close";
        async Task<long> VersionAsync() => long.Parse(Get(await (await s.Books.Admin.GetAsync($"/api/v1/organizations/{s.Books.OrgId}/ledgers/{s.Books.LedgerId}", Ct)).DataAsync(), "version"));

        var monthEnd = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, closeUrl, new { closed_through = "2026-10-31" }, ifMatch: await VersionAsync())).DataAsync();
        var yearEnd = await (await s.Books.Admin.WriteAsync(HttpMethod.Put, closeUrl, new { closed_through = "2026-12-31" }, ifMatch: await VersionAsync())).DataAsync();

        Assert.Equal(JsonValueKind.Null, monthEnd.GetProperty("warnings").ValueKind);
        Assert.Equal("2026-12-31", Get(yearEnd, "closed_through"));
        Assert.Contains("not revalued at the year end 2026-12-31", Assert.Single(yearEnd.GetProperty("warnings").EnumerateArray()).GetString());
    }

    private static IEnumerable<(string? Code, string Amount)> Flatten(JsonElement lines) =>
        lines.EnumerateArray().SelectMany(l => new[] { (l.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null, Get(l, "amount")) }
            .Concat(Flatten(l.GetProperty("children"))));

    private async Task<(SalesInvoiceTests.Setup, Guid Khr, Guid Bank)> SetUpAsync()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
        var khr = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "1120", "Wing KHR", "assets")).Id();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/bank-accounts", new { account_id = khr, bank_name = "Wing", currency = "KHR" })).DataAsync(HttpStatusCode.Created);
        return (s, khr, await PaymentTests.BankAsync(s.Books));
    }

    private static async Task<(JsonElement Journal, Dictionary<Guid, (string Dr, string Cr, string? Currency)> Lines)> JournalAsync(SalesInvoiceTests.Setup s, Guid journalId)
    {
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{journalId}", Ct)).DataAsync();
        static decimal D(JsonElement l, string p) => decimal.Parse(Get(l, p), CultureInfo.InvariantCulture);
        return (journal, journal.GetProperty("lines").EnumerateArray().GroupBy(l => l.GetProperty("account_id").GetGuid()).ToDictionary(g => g.Key, g => (
            g.Sum(l => D(l, "debit")).ToString("F2", CultureInfo.InvariantCulture),
            g.Sum(l => D(l, "credit")).ToString("F2", CultureInfo.InvariantCulture),
            g.Select(l => l.GetProperty("currency_amount")).FirstOrDefault(c => c.ValueKind == JsonValueKind.String) is { ValueKind: JsonValueKind.String } c ? c.GetString() : null)));
    }

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
