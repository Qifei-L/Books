using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Mcp;

/// <summary>Rule 5 for MCP: the tools call the same services as the API and get the same results and errors.</summary>
public sealed class McpReadToolTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_server_offers_read_only_tools_and_marks_the_writes()
    {
        await using var mcp = await ConnectAsync(Ids.Subject());

        var tools = (await mcp.ListToolsAsync(cancellationToken: Ct)).ToDictionary(t => t.Name);

        Assert.Equal(["delete_journal", "get_balance_sheet", "get_gl_detail", "get_profit_and_loss", "get_trial_balance", "list_accounts", "list_journals", "list_ledgers", "list_organizations", "save_journal"],
            tools.Keys.Order());
        Assert.All(tools.Where(t => t.Key.StartsWith("list_") || t.Key.StartsWith("get_")), t => Assert.True(t.Value.ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.False(tools["save_journal"].ProtocolTool.Annotations?.ReadOnlyHint ?? false);
        Assert.True(tools["delete_journal"].ProtocolTool.Annotations?.DestructiveHint);
    }

    [Fact]
    public async Task Tools_return_the_same_data_as_the_api()
    {
        var subject = Ids.Subject();
        var (admin, orgId, ledger) = await new AccountTests(host).LedgerAsync(subject);
        await AccountTests.CreateAsync(admin, orgId, ledger, "1000", "Cash", "assets");
        await using var mcp = await ConnectAsync(subject);

        var orgs = await CallAsync(mcp, "list_organizations", new());
        var ledgers = await CallAsync(mcp, "list_ledgers", new() { ["org_id"] = orgId });
        var accounts = await CallAsync(mcp, "list_accounts", new() { ["org_id"] = orgId, ["ledger_id"] = ledger });

        Assert.Equal(orgId, Assert.Single(orgs.EnumerateArray()).Id());
        Assert.Equal(ledger, Assert.Single(ledgers.EnumerateArray()).Id());
        var apiAccounts = await AccountTests.ListAsync(admin, orgId, ledger);
        Assert.Equal(apiAccounts.GetRawText(), accounts.GetRawText());
    }

    [Fact]
    public async Task Tools_refuse_other_organizations_and_guessed_ids_like_the_api()
    {
        var (_, aliceOrg, aliceLedger) = await new AccountTests(host).LedgerAsync();
        var bob = Ids.Subject("bob");
        var (_, bobOrg, _) = await new AccountTests(host).LedgerAsync(bob);
        await using var mcp = await ConnectAsync(bob);

        var otherOrg = await CallForErrorAsync(mcp, "list_ledgers", new() { ["org_id"] = aliceOrg });
        var guessedLedger = await CallForErrorAsync(mcp, "list_accounts", new() { ["org_id"] = bobOrg, ["ledger_id"] = aliceLedger });

        Assert.Equal("NOT_FOUND", otherOrg.GetProperty("code").GetString());
        Assert.Equal("NOT_FOUND", guessedLedger.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Tokens_only_work_at_the_entry_point_they_were_issued_for()
    {
        var subject = Ids.Subject();
        using var anonymous = host.Anonymous();
        using var apiToken = host.WithToken(host.Issuer.CreateToken(subject, TestHost.Audience));
        using var mcpToken = host.WithToken(host.Issuer.CreateToken(subject, TestHost.McpAudience));

        var noToken = await anonymous.PostAsync("/mcp", JsonBody(), Ct);
        var apiAtMcp = await apiToken.PostAsync("/mcp", JsonBody(), Ct);
        var mcpAtApi = await mcpToken.GetAsync("/api/v1/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        await apiAtMcp.AssertProblemAsync(HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
        await mcpAtApi.AssertProblemAsync(HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
    }

    private static StringContent JsonBody() => new("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", System.Text.Encoding.UTF8, "application/json");

    internal async Task<McpClient> ConnectAsync(string subject)
    {
        var http = host.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", host.Issuer.CreateToken(subject, TestHost.McpAudience, $"{subject}@example.test"));
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, cancellationToken: Ct);
    }

    internal static async Task<JsonElement> CallAsync(McpClient mcp, string tool, Dictionary<string, object?> arguments)
    {
        var result = await mcp.CallToolAsync(tool, arguments, cancellationToken: Ct);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.True(result.IsError != true, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    internal static async Task<JsonElement> CallForErrorAsync(McpClient mcp, string tool, Dictionary<string, object?> arguments)
    {
        var result = await mcp.CallToolAsync(tool, arguments, cancellationToken: Ct);
        Assert.True(result.IsError);
        return JsonDocument.Parse(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text).RootElement.Clone();
    }
}
