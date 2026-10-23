using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Reporting;

/// <summary>Rules 20, 37, 38, 39: TB and GL Detail read effective postings only, and the TB balances.</summary>
public sealed class ReportTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Trial_balance_counts_only_effective_postings_and_balances()
    {
        var books = await SeedAsync();

        var tb = await (await books.Admin.GetAsync($"{ReportsUrl(books)}/trial-balance?from=2026-10-10&to=2026-10-31", Ct)).DataAsync();

        var rows = tb.GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!);
        Assert.Equal(("150.00", "40.00", "190.00"), (Get(rows["Cash"], "opening_debit"), Get(rows["Cash"], "period_debit"), Get(rows["Cash"], "closing_debit")));
        Assert.Equal(("50.00", "40.00", "90.00"), (Get(rows["Sales"], "opening_credit"), Get(rows["Sales"], "period_credit"), Get(rows["Sales"], "closing_credit")));
        Assert.Equal("100.00", Get(rows["Retained earnings (calculated)"], "opening_credit"));
        var totals = tb.GetProperty("totals");
        Assert.Equal(Get(totals, "opening_debit"), Get(totals, "opening_credit"));
        Assert.Equal("40.00", Get(totals, "period_debit"));
        Assert.Equal(Get(totals, "closing_debit"), Get(totals, "closing_credit"));
        Assert.True(tb.GetProperty("balanced").GetBoolean());
    }

    [Fact]
    public async Task Gl_detail_shows_opening_running_balance_and_period_totals()
    {
        var books = await SeedAsync();

        var gl = await (await books.Admin.GetAsync($"{ReportsUrl(books)}/gl-detail?accountId={books.Cash}&from=2026-10-01&to=2026-10-31", Ct)).DataAsync();

        Assert.Equal("100.00", Get(gl, "opening_balance"));
        var lines = gl.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(["150.00", "190.00"], lines.Select(l => Get(l, "balance")));
        Assert.Equal("INV-1", lines[0].GetProperty("reference").GetString());
        Assert.Equal(("90.00", "0.00", "190.00"), (Get(gl, "period_debit"), Get(gl, "period_credit"), Get(gl, "closing_balance")));
    }

    [Fact]
    public async Task Income_accounts_start_each_fiscal_year_at_zero()
    {
        var books = await SeedAsync();

        var gl = await (await books.Admin.GetAsync($"{ReportsUrl(books)}/gl-detail?accountId={books.Sales}&from=2026-01-01&to=2026-12-31", Ct)).DataAsync();

        Assert.Equal("0.00", Get(gl, "opening_balance"));
        Assert.Equal("-90.00", Get(gl, "closing_balance"));
    }

    [Fact]
    public async Task Readers_see_reports_and_others_do_not()
    {
        var books = await SeedAsync();
        using var reader = await books.AddMemberAsync("read");
        using var outsider = host.Client(Ids.Subject("outsider"));
        await outsider.CreateOrganizationAsync();
        var url = $"{ReportsUrl(books)}/trial-balance?from=2026-01-01&to=2026-12-31";

        Assert.True((await (await reader.GetAsync(url, Ct)).DataAsync()).GetProperty("balanced").GetBoolean());
        await (await outsider.GetAsync(url, Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await books.Admin.GetAsync($"{ReportsUrl(books)}/trial-balance?from=2026-12-31&to=2026-01-01", Ct)).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    /// <summary>
    /// Prior year: 100 sales. This year: 50 on 5 Oct, 30 on 20 Oct edited to 40. Not effective: a draft, a failed
    /// save, a deleted journal and the superseded 30.
    /// </summary>
    private async Task<JournalBooks> SeedAsync()
    {
        var books = await JournalBooks.CreateAsync(host);
        await PostAsync(books, "2025-12-10", "100.00");
        await PostAsync(books, "2026-10-05", "50.00");
        var edited = await PostAsync(books, "2026-10-20", "30.00");
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"{books.JournalsUrl}/{edited}", Body(books, "2026-10-20", "40.00", true), ifMatch: 1)).DataAsync();
        await (await books.Admin.PostAsync(books.JournalsUrl, Body(books, "2026-10-21", "999.00", false))).DataAsync(HttpStatusCode.Created);
        await (await books.Admin.PostAsync(books.JournalsUrl, new
        {
            accounting_date = "2026-10-21",
            post = true,
            lines = new object[] { new { account_id = books.Cash, debit = "5.00" }, new { account_id = books.Sales, credit = "4.00" } },
        })).DataAsync(HttpStatusCode.UnprocessableEntity);
        var deleted = await PostAsync(books, "2026-10-22", "7.00");
        await (await books.Admin.WriteAsync(HttpMethod.Delete, $"{books.JournalsUrl}/{deleted}", null, ifMatch: 1)).DataAsync();
        return books;
    }

    private static async Task<Guid> PostAsync(JournalBooks books, string date, string amount) =>
        (await (await books.Admin.PostAsync(books.JournalsUrl, Body(books, date, amount, true))).DataAsync(HttpStatusCode.Created)).Id();

    private static object Body(JournalBooks books, string date, string amount, bool post) => new
    {
        accounting_date = date,
        post,
        reference = "INV-1",
        lines = new object[] { new { account_id = books.Cash, debit = amount }, new { account_id = books.Sales, credit = amount } },
    };

    private static string ReportsUrl(JournalBooks books) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports";

    private static string Get(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
