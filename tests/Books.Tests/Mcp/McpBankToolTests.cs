using System.Text.Json;
using Books.Tests.Documents;
using Books.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Mcp;

/// <summary>Payments and bank statements through MCP: AI drafts payments, imports statements and clears postings; figures match the API.</summary>
public sealed class McpBankToolTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_ai_drafts_payments_imports_a_statement_and_reads_the_same_reconciliation()
    {
        var subject = Ids.Subject();
        var s = await new PurchaseInvoiceTests(host).SetUpAsync(subject);
        var bank = await PaymentTests.BankAsync(s.Books);
        await (await s.Books.SaveAsync(true, ("500.00", null), (null, "500.00"))).DataAsync(System.Net.HttpStatusCode.Created);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var drafts = Data(await mcp.CallToolAsync("save_payment_drafts", Args(s, ("idempotency_key", Guid.NewGuid()), ("documents", new object[]
        {
            new Dictionary<string, object?>
            {
                ["type"] = "payment", ["bank_account_id"] = bank, ["party_id"] = s.Supplier, ["date"] = "2026-10-20",
                ["lines"] = new object[] { new Dictionary<string, object?> { ["account_id"] = s.Account("2000"), ["amount"] = "100.00" } },
            },
        })), cancellationToken: Ct));
        var imported = Data(await mcp.CallToolAsync("import_bank_statement", Args(s, ("idempotency_key", Guid.NewGuid()), ("account_id", bank),
            ("opening_balance", "0"), ("closing_balance", "495.00"), ("lines", new object[]
            {
                new Dictionary<string, object?> { ["date"] = "2026-10-15", ["amount"] = "500.00", ["description"] = "Deposit", ["external_id"] = "ABA-1" },
                new Dictionary<string, object?> { ["date"] = "2026-10-30", ["amount"] = "-5.00", ["description"] = "Fee", ["external_id"] = "ABA-2" },
            })), cancellationToken: Ct));
        var viaMcp = await McpReadToolTests.CallAsync(mcp, "get_bank_reconciliation", Args(s, ("account_id", bank), ("as_of", "2026-10-31"), ("statement_balance", "495.00")));
        var viaApi = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/bank-accounts/{bank}/reconciliation?asOf=2026-10-31&statementBalance=495.00", Ct)).DataAsync();

        Assert.Equal(("payment", "draft"), (drafts[0].GetProperty("type").GetString(), drafts[0].GetProperty("status").GetString()));
        Assert.Equal((1, 1), (imported.GetProperty("matched").GetInt32(), imported.GetProperty("drafted").GetInt32()));
        Assert.Equal(viaApi.GetRawText(), viaMcp.GetRawText());
        Assert.Equal(("500.00", "-5.00"), (viaMcp.GetProperty("cleared_balance").GetString(), viaMcp.GetProperty("difference").GetString()));
    }

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
