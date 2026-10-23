using System.Net;
using Books.Domain.Journals;
using Books.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Journal;

/// <summary>Rules 27–29: edits replace the posting exactly once, failed edits change nothing, stale edits are rejected.</summary>
public sealed class JournalEditTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_edit_replaces_the_posted_effect_exactly_once()
    {
        var books = await JournalBooks.CreateAsync(host);
        var journal = await PostAsync(books, "100.00");

        var edited = await (await EditAsync(books, journal, 1, true, "80.00")).DataAsync();

        Assert.Equal(2, edited.GetProperty("revision_no").GetInt32());
        Assert.Equal("2", edited.GetProperty("version").GetString());
        Assert.Equal((80m, 1), await EffectiveCashAsync(books));
        await using var db = host.CreateDbContext();
        var statuses = await db.JournalRevisions.Where(r => r.JournalId == journal).OrderBy(r => r.RevisionNo).Select(r => r.Status).ToListAsync(Ct);
        Assert.Equal([RevisionStatus.Superseded, RevisionStatus.Posted], statuses);
    }

    [Fact]
    public async Task A_failed_edit_leaves_the_previous_posting_unchanged()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var reader = await books.AddMemberAsync("read");
        var journal = await PostAsync(books, "100.00");

        var failed = await EditAsync(books, journal, 1, true, "80.00", "79.00");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
        Assert.Equal((100m, 1), await EffectiveCashAsync(books));
        var seenByReader = await (await reader.GetAsync($"{books.JournalsUrl}/{journal}", Ct)).DataAsync();
        Assert.Equal("posted", seenByReader.GetProperty("status").GetString());
        Assert.Equal("100.00", seenByReader.GetProperty("lines")[0].GetProperty("debit").GetString());
        var fixedEdit = await (await EditAsync(books, journal, 2, true, "90.00")).DataAsync();
        Assert.Equal("posted", fixedEdit.GetProperty("status").GetString());
        Assert.Equal((90m, 1), await EffectiveCashAsync(books));
    }

    [Fact]
    public async Task Stale_or_missing_versions_are_rejected_and_concurrent_edits_cannot_both_win()
    {
        var books = await JournalBooks.CreateAsync(host);
        var journal = await PostAsync(books, "10.00");

        await (await EditAsync(books, journal, null, true, "11.00")).AssertProblemAsync(HttpStatusCode.PreconditionRequired, "PRECONDITION_REQUIRED");
        var results = await Task.WhenAll(EditAsync(books, journal, 1, true, "12.00"), EditAsync(books, journal, 1, true, "13.00"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.PreconditionFailed);
        Assert.Equal(1, (await EffectiveCashAsync(books)).Journals);
    }

    [Fact]
    public async Task Neither_the_original_nor_the_new_date_may_be_closed()
    {
        var books = await JournalBooks.CreateAsync(host);
        var journal = await PostAsync(books, "10.00");
        await CloseThroughAsync(books, "2026-10-15");

        var edit = await (await EditAsync(books, journal, 1, true, "11.00", date: "2026-10-20")).DataAsync(HttpStatusCode.UnprocessableEntity);
        var delete = await books.Admin.WriteAsync(HttpMethod.Delete, $"{books.JournalsUrl}/{journal}", null, ifMatch: 2);

        Assert.Equal("ORIGINAL_DATE_CLOSED", Assert.Single(edit.GetProperty("issues").EnumerateArray()).GetProperty("code").GetString());
        await delete.AssertProblemAsync(HttpStatusCode.Conflict, "DATE_CLOSED");
        Assert.Equal((10m, 1), await EffectiveCashAsync(books));
    }

    [Fact]
    public async Task Deleting_removes_the_effect_and_hides_the_journal_from_others()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var reader = await books.AddMemberAsync("read");
        var journal = await PostAsync(books, "10.00");

        await (await books.Admin.WriteAsync(HttpMethod.Delete, $"{books.JournalsUrl}/{journal}", null, ifMatch: 1)).DataAsync();

        Assert.Equal((0m, 0), await EffectiveCashAsync(books));
        await (await reader.GetAsync($"{books.JournalsUrl}/{journal}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        await (await EditAsync(books, journal, 2, true, "1.00")).AssertProblemAsync(HttpStatusCode.Conflict, "JOURNAL_DELETED");
        await using var db = host.CreateDbContext();
        Assert.Equal(1, await db.AuditEvents.CountAsync(a => a.TargetId == journal && a.Action == "journal.deleted", Ct));
    }

    [Fact]
    public async Task A_draft_belongs_to_its_creator_until_it_is_posted()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var colleague = await books.AddMemberAsync("edit");
        var draft = (await (await books.SaveAsync(false, ("5.00", null))).DataAsync(HttpStatusCode.Created)).Id();

        var byColleague = await colleague.WriteAsync(HttpMethod.Put, $"{books.JournalsUrl}/{draft}", JournalBooks.Body(books, true, ("5", null), (null, "5")), ifMatch: 1);
        var posted = await (await EditAsync(books, draft, 1, true, "5.00")).DataAsync();
        var backToDraft = await EditAsync(books, draft, 2, false, "5.00");

        await byColleague.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal("posted", posted.GetProperty("status").GetString());
        await backToDraft.AssertProblemAsync(HttpStatusCode.Conflict, "JOURNAL_POSTED");
    }

    private static async Task<Guid> PostAsync(JournalBooks books, string amount) =>
        (await (await books.SaveAsync(true, (amount, null), (null, amount))).DataAsync(HttpStatusCode.Created)).Id();

    private static Task<HttpResponseMessage> EditAsync(JournalBooks books, Guid journal, long? version, bool post, string debit, string? credit = null, string date = "2026-10-15")
    {
        var body = new
        {
            accounting_date = date,
            post,
            lines = new object[]
            {
                new { account_id = books.Cash, debit },
                new { account_id = books.Sales, credit = credit ?? debit },
            },
        };
        return books.Admin.WriteAsync(HttpMethod.Put, $"{books.JournalsUrl}/{journal}", body, ifMatch: version);
    }

    /// <summary>Cash balance and number of journals counted from effective revisions only, as reports will.</summary>
    private async Task<(decimal Cash, int Journals)> EffectiveCashAsync(JournalBooks books)
    {
        await using var db = host.CreateDbContext();
        var effective = db.Journals.Where(j => j.LedgerId == books.LedgerId && j.EffectiveRevisionId != null).Select(j => j.EffectiveRevisionId);
        var cash = await db.JournalLines.Where(l => effective.Contains(l.RevisionId) && l.AccountId == books.Cash).SumAsync(l => l.Amount, Ct);
        return (cash, await effective.CountAsync(Ct));
    }

    internal async Task CloseThroughAsync(JournalBooks books, string date)
    {
        await using var db = host.CreateDbContext();
        await db.Database.ExecuteSqlAsync($"UPDATE ledgers SET closed_through = {DateOnly.Parse(date)} WHERE id = {books.LedgerId}", Ct);
    }
}
