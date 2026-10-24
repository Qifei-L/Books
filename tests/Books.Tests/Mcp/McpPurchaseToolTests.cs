using System.Net;
using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Mcp;

/// <summary>Purchases through MCP: AI clients save drafts for a person to post; AP reports match the API.</summary>
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

        var draft = Data(await SaveAsync(mcp, s, key));
        var retry = Data(await SaveAsync(mcp, s, key));
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.Url}/{draft.GetProperty("id").GetGuid()}", new
        {
            supplier_id = s.Supplier, date = "2026-10-15", post = true,
            lines = new[] { new { account_id = s.Rent, amount = "200.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = true } },
        }, ifMatch: 1)).DataAsync();
        var viaMcp = await McpReadToolTests.CallAsync(mcp, "get_ap_aging", Args(s, ("as_of", "2026-10-31")));
        var viaApi = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/reports/ap-aging?asOf=2026-10-31", Ct)).DataAsync();

        Assert.Equal(("draft", "220.00"), (draft.GetProperty("status").GetString(), draft.GetProperty("payable_total").GetString()));
        Assert.Equal(draft.GetProperty("id").GetGuid(), retry.GetProperty("id").GetGuid());
        Assert.Equal(viaApi.GetRawText(), viaMcp.GetRawText());
        Assert.Equal("220.00", viaMcp.GetProperty("totals").GetProperty("not_due").GetString());
        var listed = await McpReadToolTests.CallAsync(mcp, "list_purchase_invoices", Args(s));
        Assert.Equal("posted", Assert.Single(listed.EnumerateArray()).GetProperty("status").GetString());
    }

    private static Task<CallToolResult> SaveAsync(McpClient mcp, PurchaseInvoiceTests.Setup s, Guid key) =>
        mcp.CallToolAsync("save_purchase_invoice_draft", Args(s,
            ("idempotency_key", key),
            ("supplier_id", s.Supplier),
            ("date", "2026-10-15"),
            ("reference", "PPP-77"),
            ("lines", new object[]
            {
                new Dictionary<string, object?>
                {
                    ["account_id"] = s.Rent, ["amount"] = "200.00", ["tax_code_id"] = s.Taxes["VAT 10% (purchases)"], ["valid_tax_invoice"] = true,
                },
            })), cancellationToken: Ct).AsTask();

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
}
