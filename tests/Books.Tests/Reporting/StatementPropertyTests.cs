using System.Globalization;
using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Reporting;

/// <summary>
/// Random journals across all account types and two years, with a random fiscal-year start month. For random
/// dates the Balance Sheet always balances, the P&amp;L agrees with the calculated profit lines, and profit moves to
/// prior years exactly at the fiscal-year start (rules 20, 38, 40, 41). Seeds are fixed for replay.
/// </summary>
public sealed class StatementPropertyTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public async Task Statements_stay_consistent_for_random_books(int seed)
    {
        var random = new Random(seed);
        var fyStart = random.Next(1, 13);
        var admin = host.Client(Ids.Subject("admin"));
        var orgId = await admin.CreateOrganizationAsync();
        var ledgerId = (await (await admin.PostAsync($"/api/v1/organizations/{orgId}/companies", new { name = "Random Co", fy_start_month = fyStart }))
            .DataAsync(HttpStatusCode.Created)).Id("ledger");
        var types = new[] { "assets", "liabilities", "equity", "income", "cost_and_expense" };
        var accounts = new List<(Guid Id, string Type)>();
        for (var i = 0; i < 10; i++)
        {
            var type = types[i % types.Length];
            accounts.Add(((await AccountTests.CreateAsync(admin, orgId, ledgerId, $"{1000 + i}", $"Account {i}", type)).Id(), type));
        }

        var postings = new List<(DateOnly Date, Guid Account, decimal Amount)>();
        for (var j = 0; j < 30; j++)
        {
            var date = new DateOnly(2025, 1, 1).AddDays(random.Next(730));
            var amount = random.Next(1, 500_000) / 100m;
            var debit = accounts[random.Next(accounts.Count)];
            var credit = accounts[random.Next(accounts.Count)];
            if (debit.Id == credit.Id)
            {
                continue;
            }
            await (await admin.PostAsync($"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/journals", new
            {
                accounting_date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                post = true,
                lines = new object[]
                {
                    new { account_id = debit.Id, debit = amount.ToString("F2", CultureInfo.InvariantCulture) },
                    new { account_id = credit.Id, credit = amount.ToString("F2", CultureInfo.InvariantCulture) },
                },
            })).DataAsync(HttpStatusCode.Created);
            postings.Add((date, debit.Id, amount));
            postings.Add((date, credit.Id, -amount));
        }
        var profitAccounts = accounts.Where(a => a.Type is "income" or "cost_and_expense").Select(a => a.Id).ToHashSet();
        decimal Profit(DateOnly from, DateOnly to) =>
            -postings.Where(p => profitAccounts.Contains(p.Account) && p.Date >= from && p.Date <= to).Sum(p => p.Amount);

        for (var k = 0; k < 6; k++)
        {
            var asOf = new DateOnly(2025, 1, 1).AddDays(random.Next(760));
            var fiscalStart = new DateOnly(asOf.Month >= fyStart ? asOf.Year : asOf.Year - 1, fyStart, 1);
            var bs = await GetAsync(admin, orgId, ledgerId, $"balance-sheet?asOf={asOf:yyyy-MM-dd}");
            var equity = bs.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("type").GetString() == "equity");
            decimal Calculated(string name) => Amount(equity.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("name").GetString() == name));

            // Rule 40: assets = liabilities + equity, always.
            Assert.True(bs.GetProperty("balanced").GetBoolean(), $"seed {seed}, as of {asOf}");
            // Rule 20: current year profit is the P&L from the fiscal-year start; everything earlier is prior years.
            Assert.Equal(Profit(fiscalStart, asOf), Calculated("Current year profit (calculated)"));
            Assert.Equal(Profit(DateOnly.MinValue, fiscalStart.AddDays(-1)), Calculated("Retained earnings, prior years (calculated)"));
            // Rule 41: the P&L for the same range reports the same net profit.
            var pl = await GetAsync(admin, orgId, ledgerId, $"profit-and-loss?from={fiscalStart:yyyy-MM-dd}&to={asOf:yyyy-MM-dd}");
            Assert.Equal(Profit(fiscalStart, asOf), Amount(pl, "net_profit"));
            // Rule 38: the Trial Balance over the fiscal year to date balances.
            var tb = await GetAsync(admin, orgId, ledgerId, $"trial-balance?from={fiscalStart:yyyy-MM-dd}&to={asOf:yyyy-MM-dd}");
            Assert.True(tb.GetProperty("balanced").GetBoolean());
        }
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, Guid orgId, Guid ledgerId, string report) =>
        await (await client.GetAsync($"/api/v1/organizations/{orgId}/ledgers/{ledgerId}/reports/{report}", Ct)).DataAsync();

    private static decimal Amount(JsonElement element, string property = "amount") =>
        decimal.Parse(element.GetProperty(property).GetString()!, CultureInfo.InvariantCulture);
}
