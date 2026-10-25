using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Reporting;

/// <summary>Report templates are versioned: drafts are editable, published versions are fixed, and the ledger's version wins.</summary>
public sealed class ReportTemplateTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_kind_has_a_code_default_and_a_ledger_publishes_an_override()
    {
        var books = await JournalBooks.CreateAsync(host);
        var url = $"{ledgerUrl(books)}/report-templates";

        var listed = await (await books.Admin.GetAsync(url, Ct)).DataAsync();
        var effective = listed.GetProperty("effective").EnumerateArray().ToList();
        Assert.Equal(7, effective.Count);
        Assert.All(effective, e => Assert.Equal("default", e.GetProperty("source").GetString()));
        var pack = effective.Single(e => e.GetProperty("kind").GetString() == "client_pack");
        Assert.Equal("cover", pack.GetProperty("definition").GetProperty("sections")[0].GetProperty("key").GetString());

        var draft = await (await books.Admin.PostAsync(url, new
        {
            kind = "client_pack", name = "Short pack", effective_from = "2026-01-01",
            definition = new
            {
                title = "Short client pack",
                sections = new object[]
                {
                    new { key = "cover", enabled = true },
                    new { key = "tb", enabled = true },
                    new { key = "aging", enabled = false },
                },
            },
        })).DataAsync(HttpStatusCode.Created);
        var id = draft.Id();
        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.Equal(1, draft.GetProperty("version").GetInt64());

        // Drafts are not effective yet.
        var beforePublish = await (await books.Admin.GetAsync(url, Ct)).DataAsync();
        Assert.Equal("default", beforePublish.GetProperty("effective").EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "client_pack").GetProperty("source").GetString());

        var published = await (await books.Admin.WriteAsync(HttpMethod.Post, $"{url}/{id}/publish", null)).DataAsync();
        Assert.Equal("published", published.GetProperty("status").GetString());

        var afterPublish = await (await books.Admin.GetAsync(url, Ct)).DataAsync();
        var effectivePack = afterPublish.GetProperty("effective").EnumerateArray()
            .Single(e => e.GetProperty("kind").GetString() == "client_pack");
        Assert.Equal("ledger", effectivePack.GetProperty("source").GetString());
        Assert.Equal(id, effectivePack.GetProperty("template_id").GetGuid());
        Assert.False(effectivePack.GetProperty("definition").GetProperty("sections")[2].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task An_editor_drafts_but_only_an_administrator_publishes()
    {
        var books = await JournalBooks.CreateAsync(host);
        var url = $"{ledgerUrl(books)}/report-templates";
        var editor = await books.AddMemberAsync("edit");

        var draft = await (await editor.PostAsync(url, new
        {
            kind = "tax_workpaper", name = "Workpaper", effective_from = "2026-01-01",
            definition = new
            {
                title = "Tax workpaper",
                sections = new object[] { new { key = "boxes", enabled = true }, new { key = "issues", enabled = false } },
            },
        })).DataAsync(HttpStatusCode.Created);
        var id = draft.Id();
        await (await editor.WriteAsync(HttpMethod.Post, $"{url}/{id}/publish", null))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");

        var renamed = await (await editor.WriteAsync(HttpMethod.Put, $"{url}/{id}", new
        {
            kind = "tax_workpaper", name = "Renamed", effective_from = "2026-01-01",
            definition = new { title = "x", sections = new object[] { new { key = "boxes", enabled = true } } },
        }, ifMatch: 1)).DataAsync();
        Assert.Equal("Renamed", renamed.GetProperty("name").GetString());

        await (await books.Admin.WriteAsync(HttpMethod.Post, $"{url}/{id}/publish", null)).DataAsync();
        await (await editor.WriteAsync(HttpMethod.Put, $"{url}/{id}", new
        {
            kind = "tax_workpaper", name = "Later", effective_from = "2026-01-01",
            definition = new { title = "x", sections = new object[] { new { key = "boxes", enabled = true } } },
        }, ifMatch: 2)).AssertProblemAsync(HttpStatusCode.Conflict, "TEMPLATE_PUBLISHED");
    }

    [Fact]
    public async Task Unknown_sections_are_rejected()
    {
        var books = await JournalBooks.CreateAsync(host);
        var url = $"{ledgerUrl(books)}/report-templates";

        await (await books.Admin.PostAsync(url, new
        {
            kind = "client_pack", name = "Bad", effective_from = "2026-01-01",
            definition = new { title = "Bad", sections = new object[] { new { key = "payroll", enabled = true } } },
        })).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");

        await (await books.Admin.PostAsync(url, new
        {
            kind = "not_a_kind", name = "Bad", effective_from = "2026-01-01",
            definition = new { title = "Bad", sections = new object[] { new { key = "pl", enabled = true } } },
        })).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task The_client_pack_and_tax_workpaper_read_the_published_template()
    {
        var s = await new Books.Tests.Documents.PurchaseInvoiceTests(host).SetUpAsync();
        var url = $"{s.LedgerUrl}/report-templates";
        var draft = await (await s.Books.Admin.PostAsync(url, new
        {
            kind = "client_pack", name = "Cover and P&L only", effective_from = "2026-01-01",
            definition = new
            {
                title = "Cover and P&L only",
                sections = new object[] { new { key = "cover", enabled = true }, new { key = "pl", enabled = true } },
            },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Post, $"{url}/{draft.Id()}/publish", null)).DataAsync();
        var workpaperDraft = await (await s.Books.Admin.PostAsync(url, new
        {
            kind = "tax_workpaper", name = "Boxes only", effective_from = "2026-01-01",
            definition = new
            {
                title = "Boxes only",
                sections = new object[] { new { key = "boxes", enabled = true }, new { key = "reconciliation", enabled = false } },
            },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Post, $"{url}/{workpaperDraft.Id()}/publish", null)).DataAsync();

        var pdf = await s.Books.Admin.GetAsync(
            $"{s.LedgerUrl}/reports/client-pack.pdf?from=2026-01-01&to=2026-10-31", Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.True((await pdf.Content.ReadAsByteArrayAsync(Ct)).Length > 1000);

        var workpaper = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-10.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, workpaper.StatusCode);
    }

    private static string ledgerUrl(JournalBooks books) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";
}
