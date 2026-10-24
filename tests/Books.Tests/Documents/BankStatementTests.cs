using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;

namespace Books.Tests.Documents;

/// <summary>
/// Statements imported through the API that MCP uses (owner decisions): lines add up; matches clear; the rest become
/// draft payments and receipts to Suspense that a person posts; lines seen before are skipped.
/// </summary>
public sealed class BankStatementTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Matching_lines_clear_and_the_rest_go_to_suspense_once_a_person_posts_them()
    {
        var (s, bank) = await SeedAsync();

        var statement = await (await s.Books.Admin.PostAsync($"{Url(s, bank)}/statements", Statement("542.50"))).DataAsync(HttpStatusCode.Created);
        var before = await ReconcileAsync(s, bank);
        var posted = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/bank-statements/{statement.Id()}/post-drafts", new { })).DataAsync();
        var after = await ReconcileAsync(s, bank);
        var suspense = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/gl-detail?accountId={s.Account("2999")}&from=2026-01-01&to=2026-12-31", Ct)).DataAsync();

        Assert.Equal((2, 2, 0), (statement.GetProperty("matched").GetInt32(), statement.GetProperty("drafted").GetInt32(), statement.GetProperty("duplicates").GetInt32()));
        Assert.Equal(["matched", "matched", "drafted", "drafted"], statement.GetProperty("lines").EnumerateArray().Select(l => Get(l, "status")));
        Assert.Equal(("470.00", "72.50"), (Get(before, "cleared_balance"), Get(before, "difference")));
        Assert.Equal(2, posted.GetProperty("posted").GetArrayLength());
        Assert.Equal(("542.50", "0.00", true), (Get(after, "cleared_balance"), Get(after, "difference"), after.GetProperty("reconciled").GetBoolean()));
        Assert.Equal("-72.50", Get(suspense, "closing_balance"));
    }

    [Fact]
    public async Task Lines_imported_before_are_skipped_and_a_statement_that_does_not_add_up_is_refused()
    {
        var (s, bank) = await SeedAsync();
        await (await s.Books.Admin.PostAsync($"{Url(s, bank)}/statements", Statement("542.50"))).DataAsync(HttpStatusCode.Created);

        var again = await (await s.Books.Admin.PostAsync($"{Url(s, bank)}/statements", Statement("542.50"))).DataAsync(HttpStatusCode.Created);
        var wrong = await s.Books.Admin.PostAsync($"{Url(s, bank)}/statements", Statement("540.00"));

        Assert.Equal((0, 0, 4), (again.GetProperty("matched").GetInt32(), again.GetProperty("drafted").GetInt32(), again.GetProperty("duplicates").GetInt32()));
        await wrong.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    /// <summary>A 500 deposit by journal on 15 Oct and a 30 payment on 20 Oct on the Cash account.</summary>
    private async Task<(PurchaseInvoiceTests.Setup, Guid)> SeedAsync()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var bank = await PaymentTests.BankAsync(s.Books);
        await (await s.Books.SaveAsync(true, ("500.00", null), (null, "500.00"))).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/payments", new
        {
            bank_account_id = bank, date = "2026-10-20", lines = new[] { new { account_id = s.Supplies, amount = "30.00" } },
        })).DataAsync(HttpStatusCode.Created);
        return (s, bank);
    }

    private static object Statement(string closing) => new
    {
        opening_balance = "0.00", closing_balance = closing,
        lines = new[]
        {
            new { date = "2026-10-16", description = "Cash deposit", amount = "500.00" },
            new { date = "2026-10-21", description = "TT out", amount = "-30.00" },
            new { date = "2026-10-25", description = "Unknown transfer in", amount = "75.00" },
            new { date = "2026-10-28", description = "Monthly fee", amount = "-2.50" },
        },
    };

    private static async Task<JsonElement> ReconcileAsync(PurchaseInvoiceTests.Setup s, Guid bank) =>
        await (await s.Books.Admin.GetAsync($"{Url(s, bank)}/reconciliation?asOf=2026-10-31&statementBalance=542.50", Ct)).DataAsync();

    private static string Url(PurchaseInvoiceTests.Setup s, Guid account) => $"{s.LedgerUrl}/bank-accounts/{account}";

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
