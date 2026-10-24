using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>Fixed assets (owner default: straight line, monthly, from the month after acquisition): register, depreciation runs, disposal.</summary>
public sealed class FixedAssetTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Monthly_depreciation_runs_once_per_month_and_the_register_matches_the_accounts()
    {
        var (s, asset) = await SetUpAsync();

        var august = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/depreciation-runs", new { period = "2026-08" })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/depreciation-runs", new { period = "2026-09" })).DataAsync(HttpStatusCode.Created);
        var again = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/depreciation-runs", new { period = "2026-08" });
        var register = await RegisterAsync(s, "2026-09-30");

        Assert.Equal("50.00", Get(august, "total"));                                        // 1200 / 24 months
        await again.AssertProblemAsync(HttpStatusCode.Conflict, "DEPRECIATION_ALREADY_RUN");
        var row = Assert.Single(register.GetProperty("assets").EnumerateArray());
        Assert.Equal((asset, "100.00", "1100.00"), (row.GetProperty("id").GetGuid(), Get(row, "accumulated"), Get(row, "net_book_value")));
        Assert.True(register.GetProperty("matches_control").GetBoolean(), register.GetRawText());
    }

    [Fact]
    public async Task Disposal_takes_the_asset_off_the_books_at_net_book_value()
    {
        var (s, asset) = await SetUpAsync();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/depreciation-runs", new { period = "2026-08" })).DataAsync(HttpStatusCode.Created);
        var loss = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "7900", "Gain or loss on disposal", "cost_and_expense")).Id();

        var disposed = await (await s.Books.Admin.WriteAsync(HttpMethod.Post, $"{s.LedgerUrl}/fixed-assets/{asset}/dispose",
            new { date = "2026-10-10", gain_or_loss_account_id = loss }, ifMatch: 1)).DataAsync();
        var october = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/depreciation-runs", new { period = "2026-10" });
        var register = await RegisterAsync(s, "2026-10-31");
        var journal = await (await s.Books.Admin.GetAsync($"{s.Books.JournalsUrl}/{disposed.GetProperty("disposal_journal_id").GetGuid()}", Ct)).DataAsync();
        var lines = journal.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("account_id").GetGuid(), l => (Get(l, "debit"), Get(l, "credit")));

        Assert.Equal(("50.00", "1150.00", "1200.00"), (lines[s.Account("1590")].Item1, lines[loss].Item1, lines[s.Account("1500")].Item2));
        await october.AssertProblemAsync(HttpStatusCode.Conflict, "NOTHING_TO_DEPRECIATE");
        Assert.Equal(("0.00", true), (Get(register, "cost"), register.GetProperty("matches_control").GetBoolean()));
    }

    /// <summary>A laptop bought for 1200 on 15 July (purchase invoice to the asset account), registered over 24 months.</summary>
    private async Task<(PurchaseInvoiceTests.Setup, Guid Asset)> SetUpAsync()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            supplier_id = s.Supplier, date = "2026-07-15", lines = new[] { new { account_id = s.Account("1500"), description = "Laptop", amount = "1200.00" } },
        })).DataAsync(HttpStatusCode.Created);
        var asset = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/fixed-assets", new
        {
            code = "FA-001", name = "Laptop", acquisition_date = "2026-07-15", cost = "1200.00", useful_life_months = 24,
        })).DataAsync(HttpStatusCode.Created);
        return (s, asset.Id());
    }

    private static async Task<JsonElement> RegisterAsync(PurchaseInvoiceTests.Setup s, string asOf) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/fixed-assets?asOf={asOf}", Ct)).DataAsync();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
