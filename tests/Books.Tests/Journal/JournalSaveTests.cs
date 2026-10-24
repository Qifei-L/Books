using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Journal;

/// <summary>Rules 22, 24–26, 29: Save posts, Save Draft does not, failed saves keep an Error revision, retries post once.</summary>
public sealed class JournalSaveTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Save_posts_a_balanced_journal_with_the_next_journal_number()
    {
        var books = await JournalBooks.CreateAsync(host);

        var first = await (await books.SaveAsync(post: true, ("100.00", null), (null, "100.00"))).DataAsync(HttpStatusCode.Created);
        var second = await (await books.SaveAsync(post: true, ("5", null), (null, "5"))).DataAsync(HttpStatusCode.Created);

        Assert.Equal("posted", first.GetProperty("status").GetString());
        Assert.True(first.GetProperty("posted").GetBoolean());
        Assert.Equal(1, first.GetProperty("journal_no").GetInt64());
        Assert.Equal(2, second.GetProperty("journal_no").GetInt64());
        Assert.Equal("100.00", first.GetProperty("lines")[0].GetProperty("debit").GetString());
        Assert.Equal("100.00", first.GetProperty("lines")[1].GetProperty("credit").GetString());
    }

    [Fact]
    public async Task A_failed_save_keeps_an_error_revision_with_issues_and_posts_nothing()
    {
        var books = await JournalBooks.CreateAsync(host);

        var response = await books.SaveAsync(post: true, ("100.00", null), (null, "99.99"));
        var data = await response.DataAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("error", data.GetProperty("status").GetString());
        Assert.False(data.GetProperty("posted").GetBoolean());
        Assert.Equal("NOT_BALANCED", data.GetProperty("issues")[0].GetProperty("code").GetString());
        await using var db = host.CreateDbContext();
        var journal = await db.Journals.SingleAsync(j => j.Id == data.Id(), Ct);
        Assert.Null(journal.EffectiveRevisionId);
        Assert.Equal(1, await db.JournalRevisions.CountAsync(r => r.JournalId == journal.Id, Ct));
    }

    [Fact]
    public async Task Save_draft_keeps_an_unbalanced_journal_without_posting_it()
    {
        var books = await JournalBooks.CreateAsync(host);

        var draft = await (await books.SaveAsync(post: false, ("10.00", null))).DataAsync(HttpStatusCode.Created);

        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.False(draft.GetProperty("posted").GetBoolean());
        Assert.Contains(draft.GetProperty("issues").EnumerateArray(), i => i.GetProperty("code").GetString() == "TOO_FEW_LINES");
    }

    [Fact]
    public async Task Drafts_are_visible_only_to_their_creator_and_posted_journals_to_readers()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var reader = await books.AddMemberAsync("read");
        var draft = await (await books.SaveAsync(post: false, ("1", null), (null, "1"))).DataAsync(HttpStatusCode.Created);
        var posted = await (await books.SaveAsync(post: true, ("2", null), (null, "2"))).DataAsync(HttpStatusCode.Created);

        var readerList = await (await reader.GetAsync(books.JournalsUrl, Ct)).DataAsync();
        var creatorList = await (await books.Admin.GetAsync(books.JournalsUrl, Ct)).DataAsync();

        Assert.Equal([posted.Id()], readerList.EnumerateArray().Select(j => j.Id()));
        Assert.Equal(2, creatorList.GetArrayLength());
        await (await reader.GetAsync($"{books.JournalsUrl}/{draft.Id()}", Ct)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Retrying_a_save_with_the_same_key_posts_once_even_concurrently()
    {
        var books = await JournalBooks.CreateAsync(host);
        var key = Guid.NewGuid();
        var body = JournalBooks.Body(books, true, ("7.00", null), (null, "7.00"));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => books.Admin.PostAsync(books.JournalsUrl, body, key)));

        var ids = new HashSet<Guid>();
        foreach (var response in responses)
        {
            ids.Add((await response.DataAsync(HttpStatusCode.Created)).Id());
        }
        Assert.Single(ids);
        await using var db = host.CreateDbContext();
        Assert.Equal(1, await db.Journals.CountAsync(j => j.LedgerId == books.LedgerId, Ct));
    }

    [Fact]
    public async Task Readers_and_other_organizations_cannot_save()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var reader = await books.AddMemberAsync("read");
        using var outsider = host.Client(Ids.Subject("outsider"));
        await outsider.CreateOrganizationAsync();
        var body = JournalBooks.Body(books, true, ("1", null), (null, "1"));

        await (await reader.PostAsync(books.JournalsUrl, body)).AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await (await outsider.PostAsync(books.JournalsUrl, body)).AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Malformed_lines_are_rejected_before_anything_is_saved()
    {
        var books = await JournalBooks.CreateAsync(host);

        await (await books.SaveAsync(post: true, ("1", "1"), (null, "0"))).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await (await books.SaveAsync(post: true, ("-5", null), ("5", null))).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await (await books.SaveAsync(post: true, ("1e3", null), (null, "1000"))).AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");

        await using var db = host.CreateDbContext();
        Assert.Equal(0, await db.Journals.CountAsync(j => j.LedgerId == books.LedgerId, Ct));
    }

    [Fact]
    public async Task A_khr_ledger_rejects_fractional_amounts()
    {
        var books = await JournalBooks.CreateAsync(host, "KHR");

        var data = await (await books.SaveAsync(post: true, ("10.5", null), (null, "10.5"))).DataAsync(HttpStatusCode.UnprocessableEntity);

        Assert.All(data.GetProperty("issues").EnumerateArray(), i => Assert.Equal("TOO_MANY_DECIMALS", i.GetProperty("code").GetString()));
    }
}

/// <summary>A ledger with Cash and Sales accounts, ready for journals.</summary>
internal sealed record JournalBooks(TestHost Host, HttpClient Admin, Guid OrgId, Guid LedgerId, Guid Cash, Guid Sales)
{
    public string JournalsUrl => $"/api/v1/organizations/{OrgId}/ledgers/{LedgerId}/journals";

    public static async Task<JournalBooks> CreateAsync(TestHost host, string currency = "USD", string? subject = null)
    {
        var admin = host.Client(subject ?? Ids.Subject("admin"));
        var orgId = await admin.CreateOrganizationAsync();
        var ledgerId = (await (await admin.PostAsync($"/api/v1/organizations/{orgId}/companies", new { name = "Books Co", base_currency = currency }))
            .DataAsync(HttpStatusCode.Created)).Id("ledger");
        var cash = await AccountTests.CreateAsync(admin, orgId, ledgerId, "1000", "Cash", "assets");
        var sales = await AccountTests.CreateAsync(admin, orgId, ledgerId, "4000", "Sales", "income");
        return new JournalBooks(host, admin, orgId, ledgerId, cash.Id(), sales.Id());
    }

    /// <summary>Lines alternate Cash, Sales, Cash…; each tuple is (debit, credit).</summary>
    public static object Body(JournalBooks books, bool post, params (string? Debit, string? Credit)[] lines) => new
    {
        accounting_date = "2026-10-15",
        post,
        reference = "INV-1",
        lines = lines.Select((l, i) => new { account_id = i % 2 == 0 ? books.Cash : books.Sales, debit = l.Debit, credit = l.Credit }).ToArray(),
    };

    public Task<HttpResponseMessage> SaveAsync(bool post, params (string? Debit, string? Credit)[] lines) =>
        Admin.PostAsync(JournalsUrl, Body(this, post, lines));

    public async Task<HttpClient> AddMemberAsync(string access)
    {
        var member = Host.Client(Ids.Subject(access));
        var membership = await Host.AddMemberAsync(OrgId, member);
        await (await Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{OrgId}/memberships/{membership}/access",
            new { role = "member", grants = new[] { new { ledger_id = LedgerId, access } } }, ifMatch: 1)).DataAsync();
        return member;
    }
}
