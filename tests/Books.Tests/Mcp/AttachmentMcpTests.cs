using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Mcp;

/// <summary>M17 through MCP: AI clients upload and list attachments under the same permissions and limits.</summary>
public sealed class AttachmentMcpTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_ai_client_uploads_and_lists_an_attachment()
    {
        var subject = Ids.Subject();
        var admin = host.Client(subject);
        var orgId = await admin.CreateOrganizationAsync();
        var ledgerId = await admin.CreateLedgerAsync(orgId);
        var cash = await Books.Tests.Ledger.AccountTests.CreateAsync(admin, orgId, ledgerId, "1000", "Cash", "assets");
        var sales = await Books.Tests.Ledger.AccountTests.CreateAsync(admin, orgId, ledgerId, "4000", "Sales", "income");
        var books = new JournalBooks(host, admin, orgId, ledgerId, cash.Id(), sales.Id());
        var journal = (await (await books.SaveAsync(true, ("10.00", null), (null, "10.00"))).DataAsync(HttpStatusCode.Created)).Id();
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);
        var bytes = "%PDF-1.4 mcp attachment"u8.ToArray();

        var uploaded = await McpReadToolTests.CallAsync(mcp, "upload_attachment", new()
        {
            ["org_id"] = orgId, ["ledger_id"] = ledgerId, ["idempotency_key"] = Guid.NewGuid(),
            ["record_kind"] = "journal", ["record_id"] = journal, ["file_name"] = "receipt.pdf",
            ["content_base64"] = Convert.ToBase64String(bytes),
        });
        Assert.Equal("receipt.pdf", uploaded.GetProperty("file_name").GetString());
        Assert.Equal(bytes.Length, uploaded.GetProperty("size").GetInt64());

        var listed = await McpReadToolTests.CallAsync(mcp, "list_attachments", new()
        {
            ["org_id"] = orgId, ["ledger_id"] = ledgerId, ["record_kind"] = "journal", ["record_id"] = journal,
        });
        Assert.Equal(uploaded.GetProperty("id").GetGuid(), listed[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Read_only_members_cannot_upload_through_mcp()
    {
        var books = await JournalBooks.CreateAsync(host);
        var journal = (await (await books.SaveAsync(true, ("10.00", null), (null, "10.00"))).DataAsync(HttpStatusCode.Created)).Id();
        var subject = Ids.Subject("reader");
        using var reader = host.Client(subject);
        var membership = await host.AddMemberAsync(books.OrgId, reader);
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{books.OrgId}/memberships/{membership}/access",
            new { role = "member", grants = new[] { new { ledger_id = books.LedgerId, access = "read" } } }, ifMatch: 1)).DataAsync();
        await using var mcp = await new McpReadToolTests(host).ConnectAsync(subject);

        var error = await McpReadToolTests.CallForErrorAsync(mcp, "upload_attachment", new()
        {
            ["org_id"] = books.OrgId, ["ledger_id"] = books.LedgerId, ["idempotency_key"] = Guid.NewGuid(),
            ["record_kind"] = "journal", ["record_id"] = journal, ["file_name"] = "x.pdf",
            ["content_base64"] = Convert.ToBase64String("%PDF-1.4"u8.ToArray()),
        });

        Assert.Equal("FORBIDDEN", error.GetProperty("code").GetString());
    }
}
