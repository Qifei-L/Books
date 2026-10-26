using System.Net;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Reporting;

/// <summary>Every delivered pack is a stored report run; share links expire, revoke and are audited.</summary>
public sealed class ReportRunTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_client_pack_records_a_run_with_its_hash_and_downloads_the_same_file()
    {
        var books = await JournalBooks.CreateAsync(host);
        var url = $"{ledgerUrl(books)}/reports/client-pack.pdf?from=2026-01-01&to=2026-10-31";

        var response = await books.Admin.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var runId = Guid.Parse(response.Headers.GetValues("X-Report-Run-Id").Single());
        var pdf = await response.Content.ReadAsByteArrayAsync(Ct);

        var runs = await (await books.Admin.GetAsync($"{ledgerUrl(books)}/report-runs", Ct)).DataAsync();
        var run = runs.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == runId);
        Assert.Equal("client_pack", run.GetProperty("kind").GetString());
        Assert.Equal("ready", run.GetProperty("status").GetString());
        Assert.Equal(64, run.GetProperty("hash").GetString()!.Length);
        Assert.True(run.GetProperty("size").GetInt64() > 1000);

        var download = await books.Admin.GetAsync($"{ledgerUrl(books)}/report-runs/{runId}/download", Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task A_tax_workpaper_also_records_a_run()
    {
        var s = await new Books.Tests.Documents.PurchaseInvoiceTests(host).SetUpAsync();

        var response = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-10.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Report-Run-Id"));

        var runs = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/report-runs", Ct)).DataAsync();
        Assert.Equal("tax_workpaper", runs.EnumerateArray().Single().GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_share_link_reads_the_pack_without_sign_in_and_its_download_is_audited()
    {
        var books = await JournalBooks.CreateAsync(host);
        var runId = await ClientPackRunAsync(books);

        var share = await (await books.Admin.WriteAsync(HttpMethod.Post, $"{ledgerUrl(books)}/report-runs/{runId}/shares",
            new { days = 7, watermark = true })).DataAsync(HttpStatusCode.Created);
        var token = share.GetProperty("token").GetString()!;
        Assert.StartsWith("/share/", share.GetProperty("url").GetString());

        using var anonymous = host.Anonymous();
        var shared = await anonymous.GetAsync($"/share/{token}", Ct);
        Assert.Equal(HttpStatusCode.OK, shared.StatusCode);
        Assert.Equal("application/pdf", shared.Content.Headers.ContentType?.MediaType);
        Assert.True((await shared.Content.ReadAsByteArrayAsync(Ct)).Length > 1000);

        var download = await anonymous.GetAsync($"/share/{token}/download", Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Contains("attachment", download.Content.Headers.ContentDisposition?.DispositionType);

        await using var db = host.CreateDbContext();
        var audits = await db.AuditEvents.Where(a => a.Action == "report_share.downloaded" && a.TargetId == share.Id()).CountAsync(Ct);
        Assert.Equal(2, audits);
    }

    [Fact]
    public async Task A_share_can_be_revoked_and_one_expired_in_the_database_stops_working()
    {
        var books = await JournalBooks.CreateAsync(host);
        var runId = await ClientPackRunAsync(books);
        var url = $"{ledgerUrl(books)}/report-runs/{runId}/shares";

        var revoked = await (await books.Admin.WriteAsync(HttpMethod.Post, url, new { days = 7 })).DataAsync(HttpStatusCode.Created);
        using var anonymous = host.Anonymous();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/share/{revoked.GetProperty("token").GetString()}", Ct)).StatusCode);

        await (await books.Admin.WriteAsync(HttpMethod.Delete,
            $"{ledgerUrl(books)}/report-shares/{revoked.Id()}", null)).DataAsync();
        await (await anonymous.GetAsync($"/share/{revoked.GetProperty("token").GetString()}", Ct))
            .AssertProblemAsync(HttpStatusCode.Gone, "SHARE_REVOKED");

        var expired = await (await books.Admin.WriteAsync(HttpMethod.Post, url, new { days = 1 })).DataAsync(HttpStatusCode.Created);
        await using (var db = host.CreateDbContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE report_shares SET expires_at = now() - interval '1 day' WHERE id = {expired.Id()}", Ct);
        }
        await (await anonymous.GetAsync($"/share/{expired.GetProperty("token").GetString()}", Ct))
            .AssertProblemAsync(HttpStatusCode.Gone, "SHARE_EXPIRED");
    }

    [Fact]
    public async Task Share_creation_needs_edit_access_and_runs_are_invisible_outside_the_ledger()
    {
        var books = await JournalBooks.CreateAsync(host);
        var runId = await ClientPackRunAsync(books);
        var shareUrl = $"{ledgerUrl(books)}/report-runs/{runId}/shares";

        using var reader = await books.AddMemberAsync("read");
        await (await reader.WriteAsync(HttpMethod.Post, shareUrl, new { days = 7 }))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        var listed = await reader.GetAsync($"{ledgerUrl(books)}/report-runs", Ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);

        using var anonymous = host.Anonymous();
        await (await anonymous.GetAsync($"{ledgerUrl(books)}/report-runs", Ct))
            .AssertProblemAsync(HttpStatusCode.Unauthorized, "UNAUTHENTICATED");

        var other = await JournalBooks.CreateAsync(host);
        await (await other.Admin.GetAsync($"{ledgerUrl(books)}/report-runs/{runId}/download", Ct))
            .AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    private static async Task<Guid> ClientPackRunAsync(JournalBooks books)
    {
        var response = await books.Admin.GetAsync($"{ledgerUrl(books)}/reports/client-pack.pdf?from=2026-01-01&to=2026-10-31", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Guid.Parse(response.Headers.GetValues("X-Report-Run-Id").Single());
    }

    private static string ledgerUrl(JournalBooks books) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";
}
