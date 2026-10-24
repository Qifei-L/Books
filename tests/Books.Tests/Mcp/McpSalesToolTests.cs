using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Mcp;

/// <summary>Sales through MCP: batches of drafts, all or none; a person posts; AR reports match the API.</summary>
public sealed class McpSalesToolTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Ai_drafts_are_posted_by_a_person_and_the_ar_aging_matches_the_api()
    {
        var subject = Ids.Subject();
        var s = await new SalesInvoiceTests(host).SetUpAsync(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var drafts = Data(await mcp.CallToolAsync("save_sales_drafts", Args(s, ("idempotency_key", Guid.NewGuid()), ("documents", new[]
        {
            Document(s, "invoice", "1000.00"),
            Document(s, "credit_note", "100.00"),
        })), cancellationToken: Ct));
        foreach (var (draft, path) in drafts.EnumerateArray().Zip(["sales-invoices", "credit-notes"]))
        {
            await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/{path}/{draft.Id()}", new
            {
                customer_id = s.Customer, date = "2026-10-15", post = true,
                lines = new[] { new { account_id = s.Books.Sales, amount = draft.GetProperty("net_total").GetString(), tax_code_id = s.Taxes["VAT 10% (sales)"] } },
            }, ifMatch: 1)).DataAsync();
        }
        var viaMcp = await McpReadToolTests.CallAsync(mcp, "get_ar_aging", Args(s, ("as_of", "2026-10-31")));
        var viaApi = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/ar-aging?asOf=2026-10-31", Ct)).DataAsync();
        var listed = await McpReadToolTests.CallAsync(mcp, "list_sales_invoices", Args(s));

        Assert.Equal(["draft", "draft"], drafts.EnumerateArray().Select(d => d.GetProperty("status").GetString()));
        Assert.Equal(viaApi.GetRawText(), viaMcp.GetRawText());
        Assert.Equal("990.00", viaMcp.GetProperty("totals").GetProperty("total").GetString());
        Assert.Equal(["posted", "posted"], listed.EnumerateArray().Select(d => d.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task An_unknown_type_rejects_the_whole_batch()
    {
        var subject = Ids.Subject();
        var s = await new SalesInvoiceTests(host).SetUpAsync(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var result = await mcp.CallToolAsync("save_sales_drafts", Args(s, ("idempotency_key", Guid.NewGuid()), ("documents", new[]
        {
            Document(s, "invoice", "10.00"),
            Document(s, "quote", "10.00"),
        })), cancellationToken: Ct);

        Assert.True(result.IsError);
        var error = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement;
        Assert.Equal("BATCH_REJECTED", error.GetProperty("code").GetString());
        Assert.Equal(2, Assert.Single(error.GetProperty("issues").EnumerateArray()).GetProperty("document").GetInt32());
        Assert.Equal(0, (await McpReadToolTests.CallAsync(mcp, "list_sales_invoices", Args(s))).GetArrayLength());
    }

    private static Dictionary<string, object?> Document(SalesInvoiceTests.Setup s, string type, string amount) => new()
    {
        ["type"] = type,
        ["customer_id"] = s.Customer,
        ["date"] = "2026-10-15",
        ["lines"] = new object[] { new Dictionary<string, object?> { ["account_id"] = s.Books.Sales, ["amount"] = amount, ["tax_code_id"] = s.Taxes["VAT 10% (sales)"] } },
    };

    private static Dictionary<string, object?> Args(SalesInvoiceTests.Setup s, params (string Name, object? Value)[] more)
    {
        var args = new Dictionary<string, object?> { ["org_id"] = s.Books.OrgId, ["ledger_id"] = s.Books.LedgerId };
        foreach (var (name, value) in more)
        {
            args[name] = value;
        }
        return args;
    }

    private static JsonElement Data(CallToolResult result)
    {
        var text = ((TextContentBlock)result.Content[0]).Text;
        Assert.False(result.IsError ?? false, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }
}
