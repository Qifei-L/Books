using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Books.Tests.Mcp;

/// <summary>Rules 5, 26, 29 through MCP: same validation, permissions, idempotency and reports as the API; AI clients draft only.</summary>
public sealed class McpJournalToolTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_draft_from_mcp_posted_by_a_person_shows_in_the_same_trial_balance_as_the_api()
    {
        var subject = Ids.Subject();
        var books = await JournalBooksFor(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var saved = await SaveAsync(mcp, books, Guid.NewGuid(), "25.00", "25.00");
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"{books.JournalsUrl}/{Data(saved).GetProperty("id").GetGuid()}",
            JournalBooks.Body(books, true, ("25.00", null), (null, "25.00")), ifMatch: 1)).DataAsync();
        var viaMcp = await McpReadToolTests.CallAsync(mcp, "get_trial_balance", Args(books, ("from", "2026-10-01"), ("to", "2026-10-31")));
        var viaApi = await (await books.Admin.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/trial-balance?from=2026-10-01&to=2026-10-31", Ct)).DataAsync();

        Assert.False(saved.IsError ?? false);
        Assert.Equal(viaApi.GetRawText(), viaMcp.GetRawText());
        Assert.True(viaMcp.GetProperty("balanced").GetBoolean());
    }

    [Fact]
    public async Task Retrying_with_the_same_idempotency_key_posts_once()
    {
        var subject = Ids.Subject();
        var books = await JournalBooksFor(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);
        var key = Guid.NewGuid();

        var first = Data(await SaveAsync(mcp, books, key, "10.00", "10.00"));
        var retry = Data(await SaveAsync(mcp, books, key, "10.00", "10.00"));
        var reused = await SaveAsync(mcp, books, key, "11.00", "11.00");

        Assert.Equal(first.GetProperty("id").GetGuid(), retry.GetProperty("id").GetGuid());
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", Error(reused).GetProperty("code").GetString());
        var journals = await McpReadToolTests.CallAsync(mcp, "list_journals", Args(books));
        Assert.Equal(1, journals.GetArrayLength());
    }

    [Fact]
    public async Task An_unbalanced_draft_is_saved_with_the_issues_that_block_posting()
    {
        var subject = Ids.Subject();
        var books = await JournalBooksFor(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var draft = Data(await SaveAsync(mcp, books, Guid.NewGuid(), "10.00", "9.00"));

        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.Equal("NOT_BALANCED", draft.GetProperty("issues")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Ai_clients_cannot_post_or_delete_a_posted_journal_even_as_administrators()
    {
        var subject = Ids.Subject();
        var books = await JournalBooksFor(subject);
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);
        var posted = (await (await books.SaveAsync(true, ("3.00", null), (null, "3.00"))).DataAsync(HttpStatusCode.Created)).Id();

        var post = await SaveAsync(mcp, books, Guid.NewGuid(), "1.00", "1.00", post: true);
        var delete = await mcp.CallToolAsync("delete_journal", Args(books, ("journal_id", posted), ("idempotency_key", Guid.NewGuid()), ("expected_version", 1)), cancellationToken: Ct);

        Assert.Equal("AGENT_CANNOT_POST", Error(post).GetProperty("code").GetString());
        Assert.Equal("AGENT_CANNOT_POST", Error(delete).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Read_only_members_cannot_save_through_mcp_either()
    {
        var books = await JournalBooksFor(Ids.Subject());
        var readerSubject = Ids.Subject("reader");
        using var reader = host.Client(readerSubject);
        var membership = await host.AddMemberAsync(books.OrgId, reader);
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{books.OrgId}/memberships/{membership}/access",
            new { role = "member", grants = new[] { new { ledger_id = books.LedgerId, access = "read" } } }, ifMatch: 1)).DataAsync();
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(readerSubject);

        var result = await SaveAsync(mcp, books, Guid.NewGuid(), "1.00", "1.00");

        Assert.Equal("FORBIDDEN", Error(result).GetProperty("code").GetString());
    }

    private async Task<JournalBooks> JournalBooksFor(string subject)
    {
        var admin = host.Client(subject);
        var orgId = await admin.CreateOrganizationAsync();
        var ledgerId = await admin.CreateLedgerAsync(orgId);
        var cash = await Books.Tests.Ledger.AccountTests.CreateAsync(admin, orgId, ledgerId, "1000", "Cash", "assets");
        var sales = await Books.Tests.Ledger.AccountTests.CreateAsync(admin, orgId, ledgerId, "4000", "Sales", "income");
        return new JournalBooks(host, admin, orgId, ledgerId, cash.Id(), sales.Id());
    }

    private static Task<CallToolResult> SaveAsync(McpClient mcp, JournalBooks books, Guid key, string debit, string credit, bool post = false) =>
        mcp.CallToolAsync("save_journal", Args(books,
            ("idempotency_key", key),
            ("accounting_date", "2026-10-15"),
            ("post", post),
            ("lines", new object[]
            {
                new Dictionary<string, object?> { ["account_id"] = books.Cash, ["debit"] = debit },
                new Dictionary<string, object?> { ["account_id"] = books.Sales, ["credit"] = credit },
            })), cancellationToken: Ct).AsTask();

    private static Dictionary<string, object?> Args(JournalBooks books, params (string Name, object? Value)[] more)
    {
        var args = new Dictionary<string, object?> { ["org_id"] = books.OrgId, ["ledger_id"] = books.LedgerId };
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
        Assert.True(result.IsError);
        return JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement.Clone();
    }
}
