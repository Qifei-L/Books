using System.Net;
using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Mcp;

/// <summary>
/// Purchases through MCP: AI clients save batches of drafts, all or none, for a person to post; AP reports match the API.
/// </summary>
public sealed class McpPurchaseToolTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_ai_draft_is_posted_by_a_person_and_then_shows_in_the_ap_aging()
    {
        var subject = Ids.Subject();
        var s = await new PurchaseInvoiceTests(host).SetUpAsync(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);
        var key = Guid.NewGuid();

        var drafts = Data(await SaveAsync(mcp, s, key, Invoice(s, "200.00"), DebitNote(s, "50.00")));
        var retry = Data(await SaveAsync(mcp, s, key, Invoice(s, "200.00"), DebitNote(s, "50.00")));
        var draft = drafts[0];
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.Url}/{draft.Id()}", new
        {
            supplier_id = s.Supplier, date = "2026-10-15", post = true,
            lines = new[] { new { account_id = s.Rent, amount = "200.00", tax_code_id = s.Taxes["VAT 10% (purchases)"] } },
        }, ifMatch: 1)).DataAsync();
        var viaMcp = await McpReadToolTests.CallAsync(mcp, "get_ap_aging", Args(s, ("as_of", "2026-10-31")));
        var viaApi = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/ap-aging?asOf=2026-10-31", Ct)).DataAsync();

        Assert.Equal([("invoice", "draft", "220.00"), ("debit_note", "draft", "55.00")],
            drafts.EnumerateArray().Select(d => (Get(d, "type"), Get(d, "status"), Get(d, "payable_total"))));
        Assert.Equal(drafts.GetRawText(), retry.GetRawText());
        Assert.Equal(viaApi.GetRawText(), viaMcp.GetRawText());
        Assert.Equal("220.00", Get(viaMcp.GetProperty("totals"), "not_due"));
        var listed = await McpReadToolTests.CallAsync(mcp, "list_purchase_invoices", Args(s));
        Assert.Equal("posted", Get(Assert.Single(listed.EnumerateArray()), "status"));
    }

    [Fact]
    public async Task A_batch_with_errors_saves_nothing_and_lists_every_error()
    {
        var subject = Ids.Subject();
        var s = await new PurchaseInvoiceTests(host).SetUpAsync(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);
        var unknownAccount = Invoice(s, "10.00");
        unknownAccount["lines"] = new object[] { new Dictionary<string, object?> { ["account_id"] = Guid.NewGuid(), ["amount"] = "10.00",  } };
        var unknownSupplier = Invoice(s, "20.00");
        unknownSupplier["supplier_id"] = Guid.NewGuid();
        var closedPeriod = Invoice(s, "30.00"); // a business problem: would only stay a draft, not an error
        closedPeriod["date"] = "2020-01-01";

        var error = Error(await SaveAsync(mcp, s, Guid.NewGuid(), Invoice(s, "5.00"), unknownAccount, closedPeriod, unknownSupplier));

        Assert.Equal("BATCH_REJECTED", Get(error, "code"));
        Assert.Equal([2, 4], error.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("document").GetInt32()));
        var listed = await McpReadToolTests.CallAsync(mcp, "list_purchase_invoices", Args(s));
        Assert.Equal(0, listed.GetArrayLength());
    }

    [Fact]
    public async Task Batches_are_limited_to_100_documents()
    {
        var subject = Ids.Subject();
        var s = await new PurchaseInvoiceTests(host).SetUpAsync(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var error = Error(await SaveAsync(mcp, s, Guid.NewGuid(), Enumerable.Range(0, 101).Select(_ => Invoice(s, "1.00")).ToArray()));

        Assert.Equal("INVALID_ARGUMENT", Get(error, "code"));
    }

    private static Dictionary<string, object?> Invoice(PurchaseInvoiceTests.Setup s, string amount, string type = "invoice") => new()
    {
        ["type"] = type,
        ["supplier_id"] = s.Supplier,
        ["date"] = "2026-10-15",
        ["reference"] = "PPP-77",
        ["lines"] = new object[]
        {
            new Dictionary<string, object?> { ["account_id"] = s.Rent, ["amount"] = amount, ["tax_code_id"] = s.Taxes["VAT 10% (purchases)"],  },
        },
    };

    private static Dictionary<string, object?> DebitNote(PurchaseInvoiceTests.Setup s, string amount) => Invoice(s, amount, "debit_note");

    private static Task<CallToolResult> SaveAsync(McpClient mcp, PurchaseInvoiceTests.Setup s, Guid key, params Dictionary<string, object?>[] documents) =>
        mcp.CallToolAsync("save_purchase_drafts", Args(s, ("idempotency_key", key), ("documents", documents)), cancellationToken: Ct).AsTask();

    private static Dictionary<string, object?> Args(PurchaseInvoiceTests.Setup s, params (string Name, object? Value)[] more)
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

    private static JsonElement Error(CallToolResult result)
    {
        var text = ((TextContentBlock)result.Content[0]).Text;
        Assert.True(result.IsError, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
