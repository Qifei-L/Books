using Books.Tests.Infrastructure;
using Books.Tests.Reporting;

namespace Books.Tests.Mcp;

/// <summary>Rule 5: statements through MCP equal the API's.</summary>
public sealed class McpStatementToolTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Balance_sheet_and_profit_and_loss_through_mcp_equal_the_api()
    {
        var subject = Ids.Subject("statements");
        var books = await new StatementTests(host).SeedAsync(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);
        var ids = new Dictionary<string, object?> { ["org_id"] = books.OrgId, ["ledger_id"] = books.LedgerId };

        var bs = await McpReadToolTests.CallAsync(mcp, "get_balance_sheet", new(ids) { ["as_of"] = "2026-12-31" });
        var pl = await McpReadToolTests.CallAsync(mcp, "get_profit_and_loss", new(ids) { ["from"] = "2026-01-01", ["to"] = "2026-12-31" });

        Assert.Equal((await StatementTests.GetAsync(books, "balance-sheet?asOf=2026-12-31")).GetRawText(), bs.GetRawText());
        Assert.Equal((await StatementTests.GetAsync(books, "profit-and-loss?from=2026-01-01&to=2026-12-31")).GetRawText(), pl.GetRawText());
    }
}
