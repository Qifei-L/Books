using System.Net;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Journal;

/// <summary>Rules 10, 12, 13, 27: administrators Close and reopen; setup is fixed after the first posting.</summary>
public sealed class LedgerCloseTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Close_blocks_journals_on_or_before_the_date_and_reopening_allows_them_again()
    {
        var books = await JournalBooks.CreateAsync(host);

        await (await CloseAsync(books, "2026-10-15", version: 1)).DataAsync();
        var onCloseDate = await books.SaveAsync(true, ("1.00", null), (null, "1.00"));
        var after = await SaveOnAsync(books, "2026-10-16");
        await (await CloseAsync(books, null, version: 2)).DataAsync();
        var reopened = await books.SaveAsync(true, ("1.00", null), (null, "1.00"));

        Assert.Equal("DATE_CLOSED", (await onCloseDate.DataAsync(HttpStatusCode.UnprocessableEntity)).GetProperty("issues")[0].GetProperty("code").GetString());
        await after.DataAsync(HttpStatusCode.Created);
        await reopened.DataAsync(HttpStatusCode.Created);
        await using var db = host.CreateDbContext();
        var actions = await db.AuditEvents.Where(a => a.TargetId == books.LedgerId).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct);
        Assert.Equal(["ledger.created", "ledger.closed", "ledger.reopened"], actions);
    }

    [Fact]
    public async Task Only_administrators_close_and_they_need_the_current_version()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var editor = await books.AddMemberAsync("edit");

        await (await editor.WriteAsync(HttpMethod.Put, CloseUrl(books), new { closed_through = "2026-01-31" }, ifMatch: 1))
            .AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await CloseAsync(books, "2026-01-31", version: null)).AssertProblemAsync(HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED");
        await (await CloseAsync(books, "2026-01-31", version: 9)).AssertProblemAsync(HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
    }

    [Fact]
    public async Task Base_currency_and_fiscal_year_change_only_before_the_first_posting()
    {
        var books = await JournalBooks.CreateAsync(host);
        var settings = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}";

        var beforePosting = await (await books.Admin.WriteAsync(HttpMethod.Patch, settings, new { base_currency = "KHR", fy_start_month = 4 }, ifMatch: 1)).DataAsync();
        await (await books.Admin.WriteAsync(HttpMethod.Patch, settings, new { base_currency = "USD" }, ifMatch: 2)).DataAsync();
        var journal = (await (await books.SaveAsync(true, ("1.00", null), (null, "1.00"))).DataAsync(HttpStatusCode.Created)).Id();
        await (await books.Admin.WriteAsync(HttpMethod.Delete, $"{books.JournalsUrl}/{journal}", null, ifMatch: 1)).DataAsync();

        var currency = await books.Admin.WriteAsync(HttpMethod.Patch, settings, new { base_currency = "KHR" }, ifMatch: 3);
        var month = await books.Admin.WriteAsync(HttpMethod.Patch, settings, new { fy_start_month = 7 }, ifMatch: 3);

        Assert.Equal("KHR", beforePosting.GetProperty("base_currency").GetString());
        Assert.Equal(4, beforePosting.GetProperty("fy_start_month").GetInt32());
        await currency.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await month.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task An_edit_racing_a_close_either_commits_first_or_is_refused()
    {
        for (var round = 0; round < 5; round++)
        {
            var books = await JournalBooks.CreateAsync(host);
            var journal = (await (await books.SaveAsync(true, ("10.00", null), (null, "10.00"))).DataAsync(HttpStatusCode.Created)).Id();
            var edit = books.Admin.WriteAsync(HttpMethod.Put, $"{books.JournalsUrl}/{journal}", JournalBooks.Body(books, true, ("20.00", null), (null, "20.00")), ifMatch: 1);
            var close = CloseAsync(books, "2026-10-31", version: 1);

            await Task.WhenAll(edit, close);

            Assert.Equal(HttpStatusCode.OK, close.Result.StatusCode);
            await using var db = host.CreateDbContext();
            var events = await db.AuditEvents.Where(a => a.OrgId == books.OrgId && (a.Action == "journal.posted" || a.Action == "ledger.closed"))
                .OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct);
            if (edit.Result.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal(["journal.posted", "journal.posted", "ledger.closed"], events);
            }
            else
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, edit.Result.StatusCode);
                Assert.Equal(["journal.posted", "ledger.closed"], events);
            }
        }
    }

    private static string CloseUrl(JournalBooks books) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/close";

    private static Task<HttpResponseMessage> CloseAsync(JournalBooks books, string? date, long? version) =>
        books.Admin.WriteAsync(HttpMethod.Put, CloseUrl(books), new { closed_through = date }, ifMatch: version);

    private static Task<HttpResponseMessage> SaveOnAsync(JournalBooks books, string date) =>
        books.Admin.PostAsync(books.JournalsUrl, new
        {
            accounting_date = date,
            post = true,
            lines = new object[] { new { account_id = books.Cash, debit = "1.00" }, new { account_id = books.Sales, credit = "1.00" } },
        });
}
