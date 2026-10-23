using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Reporting;

/// <summary>Rules 14, 20, 40, 41: P&amp;L and Balance Sheet with group subtotals, calculated year-end, contra balances.</summary>
public sealed class StatementTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Profit_and_loss_nets_income_and_cost_with_contra_accounts_negative()
    {
        var books = await SeedAsync();

        var pl = await GetAsync(books, "profit-and-loss?from=2026-01-01&to=2026-12-31");

        var income = Section(pl, "income");
        Assert.Equal("450.00", income.GetProperty("total").GetString());
        Assert.Equal("-50.00", Line(Line(income, "Revenue"), "Sales returns").GetProperty("amount").GetString());
        Assert.Equal("120.00", Section(pl, "cost_and_expense").GetProperty("total").GetString());
        Assert.Equal("330.00", pl.GetProperty("net_profit").GetString());
    }

    [Fact]
    public async Task Profit_and_loss_may_span_fiscal_years()
    {
        var books = await SeedAsync();

        var pl = await GetAsync(books, "profit-and-loss?from=2025-01-01&to=2026-12-31");

        Assert.Equal("530.00", pl.GetProperty("net_profit").GetString());
    }

    [Fact]
    public async Task Balance_sheet_balances_with_calculated_year_end_and_nested_subtotals()
    {
        var books = await SeedAsync();

        var bs = await GetAsync(books, "balance-sheet?asOf=2026-12-31");

        var assets = Section(bs, "assets");
        var current = Line(assets, "Current assets");
        Assert.Equal("1650.00", current.GetProperty("amount").GetString());
        Assert.Equal("1500.00", Line(current, "Bank accounts").GetProperty("amount").GetString());
        var equity = Section(bs, "equity");
        Assert.Equal("0.00", Line(equity, "Retained earnings").GetProperty("amount").GetString());
        Assert.Equal("200.00", Line(equity, "Retained earnings, prior years (calculated)").GetProperty("amount").GetString());
        Assert.Equal("330.00", Line(equity, "Current year profit (calculated)").GetProperty("amount").GetString());
        Assert.Equal("1650.00", bs.GetProperty("total_assets").GetString());
        Assert.Equal("1650.00", bs.GetProperty("total_liabilities_and_equity").GetString());
        Assert.True(bs.GetProperty("balanced").GetBoolean());
    }

    [Fact]
    public async Task Year_end_moves_profit_into_prior_years_without_any_closing_journal()
    {
        var books = await SeedAsync();

        var lastYear = await GetAsync(books, "balance-sheet?asOf=2025-12-31");
        var thisYear = await GetAsync(books, "balance-sheet?asOf=2026-01-01");

        Assert.Equal("200.00", Line(Section(lastYear, "equity"), "Current year profit (calculated)").GetProperty("amount").GetString());
        Assert.Equal("0.00", Line(Section(lastYear, "equity"), "Retained earnings, prior years (calculated)").GetProperty("amount").GetString());
        Assert.Equal("200.00", Line(Section(thisYear, "equity"), "Retained earnings, prior years (calculated)").GetProperty("amount").GetString());
        Assert.Equal("0.00", Line(Section(thisYear, "equity"), "Current year profit (calculated)").GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Accounts_with_zero_balance_are_listed()
    {
        var books = await SeedAsync();

        var bs = await GetAsync(books, "balance-sheet?asOf=2024-12-31");

        Assert.Equal("0.00", Line(Line(Section(bs, "assets"), "Current assets"), "Cash").GetProperty("amount").GetString());
        Assert.True(bs.GetProperty("balanced").GetBoolean());
    }

    [Fact]
    public async Task Outsiders_cannot_read_statements()
    {
        var books = await SeedAsync();
        using var outsider = host.Client(Ids.Subject("outsider"));
        await outsider.CreateOrganizationAsync();

        var response = await outsider.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/balance-sheet?asOf=2026-12-31", Ct);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    internal sealed record Books(HttpClient Admin, Guid OrgId, Guid LedgerId);

    /// <summary>
    /// 2025: capital 1000 to the bank, sale 300 cash, rent 100 cash (profit 200).
    /// 2026: sale 500 to the bank, sales return 50 cash, rent 120 on credit (profit 330).
    /// </summary>
    internal async Task<Books> SeedAsync(string? subject = null)
    {
        var admin = host.Client(subject ?? Ids.Subject("admin"));
        var orgId = await admin.CreateOrganizationAsync();
        var ledgerId = await admin.CreateLedgerAsync(orgId);
        var groups = $"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/groups";
        async Task<Guid> Group(string name, string type, Guid? parent = null) =>
            (await (await admin.PostAsync(groups, new { name, type, parent_id = parent })).DataAsync(HttpStatusCode.Created)).Id();
        async Task<Guid> Account(string code, string name, string type, Guid? group = null) =>
            (await AccountTests.CreateAsync(admin, orgId, ledgerId, code, name, type, group)).Id();

        var current = await Group("Current assets", "assets");
        var bankGroup = await Group("Bank accounts", "assets", current);
        var revenue = await Group("Revenue", "income");
        var cash = await Account("1000", "Cash", "assets", current);
        var bank = await Account("1100", "Bank", "assets", bankGroup);
        var payable = await Account("2000", "Payable", "liabilities");
        var capital = await Account("3000", "Capital", "equity");
        await Account("3100", "Retained earnings", "equity");
        var sales = await Account("4000", "Sales", "income", revenue);
        var returns = await Account("4900", "Sales returns", "income", revenue);
        var rent = await Account("5000", "Rent", "cost_and_expense");

        var journals = $"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/journals";
        async Task Post(string date, Guid debit, Guid credit, string amount) =>
            await (await admin.PostAsync(journals, new
            {
                accounting_date = date,
                post = true,
                lines = new object[] { new { account_id = debit, debit = amount }, new { account_id = credit, credit = amount } },
            })).DataAsync(HttpStatusCode.Created);
        await Post("2025-06-01", bank, capital, "1000.00");
        await Post("2025-07-01", cash, sales, "300.00");
        await Post("2025-08-01", rent, cash, "100.00");
        await Post("2026-02-01", bank, sales, "500.00");
        await Post("2026-02-10", returns, cash, "50.00");
        await Post("2026-03-01", rent, payable, "120.00");
        return new Books(admin, orgId, ledgerId);
    }

    internal static async Task<JsonElement> GetAsync(Books books, string report) =>
        await (await books.Admin.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/{report}", Ct)).DataAsync();

    internal static JsonElement Section(JsonElement statement, string type) =>
        statement.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("type").GetString() == type);

    /// <summary>Finds a direct child line by name, inside a section ("lines") or a group ("children").</summary>
    internal static JsonElement Line(JsonElement parent, string name) =>
        parent.GetProperty(parent.TryGetProperty("lines", out _) ? "lines" : "children").EnumerateArray()
            .Single(l => l.GetProperty("name").GetString() == name);
}
