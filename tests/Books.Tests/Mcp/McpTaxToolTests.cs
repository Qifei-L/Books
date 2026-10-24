using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Mcp;

/// <summary>Tax returns through MCP: the same figures as the API; AI clients draft definitions, people publish and file.</summary>
public sealed class McpTaxToolTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_return_over_mcp_equals_the_api_and_an_ai_draft_definition_previews_it()
    {
        var subject = Ids.Subject();
        var s = await new TaxReturnTests(host).SeedAsync(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var viaMcp = await McpReadToolTests.CallAsync(mcp, "get_tax_return", Args(s, ("period", "2026-10")));
        var viaApi = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10");
        var definitions = await McpReadToolTests.CallAsync(mcp, "list_tax_return_definitions", Args(s));
        var spec = JsonNode.Parse(definitions[0].GetProperty("definition").GetRawText())!.AsObject();
        ((JsonArray)spec["boxes"]!).Add(JsonNode.Parse("""{"key": "export_sales", "section": "VAT", "label": "Exports", "lines": {"side": "sale", "tax_kinds": ["vat_zero_rated"]}}"""));
        var draft = await mcp.CallToolAsync("save_tax_return_definition_draft",
            Args(s, ("idempotency_key", Guid.NewGuid()), ("effective_from", "2026-11"), ("definition", spec)), cancellationToken: Ct);
        var preview = await McpReadToolTests.CallAsync(mcp, "get_tax_return", Args(s, ("period", "2026-10"), ("definition_version", 1)));
        var lines = await McpReadToolTests.CallAsync(mcp, "get_tax_return_box_lines", Args(s, ("period", "2026-10"), ("box_key", "t701_15")));

        Assert.Equal(viaApi.GetRawText(), viaMcp.GetRawText());
        Assert.False(draft.IsError ?? false, ((TextContentBlock)draft.Content[0]).Text);
        Assert.Equal("250.00", TaxReturnTests.Boxes(preview)["export_sales"].Usd);
        Assert.Equal(2, lines.GetProperty("lines").GetArrayLength());
    }

    [Fact]
    public async Task The_return_exports_to_excel()
    {
        var s = await new TaxReturnTests(host).SeedAsync();

        var response = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-10.xlsx", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadAsByteArrayAsync(Ct)).Length > 1000);
    }

    private static Dictionary<string, object?> Args(SalesInvoiceTests.Setup s, params (string Name, object? Value)[] more)
    {
        var args = new Dictionary<string, object?> { ["org_id"] = s.Books.OrgId, ["ledger_id"] = s.Books.LedgerId };
        foreach (var (name, value) in more)
        {
            args[name] = value;
        }
        return args;
    }
}
