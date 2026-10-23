using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;
using Books.Tests.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Books.Tests.Documents;

/// <summary>Rules 31 and 32 through the first document type: a bank transfer and its journal change together.</summary>
public sealed class BankTransferTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_transfer_posts_its_journal_with_the_fee_from_the_sending_bank()
    {
        var setup = await SetUpAsync();

        var transfer = await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "500.00", fee: "2.00"))).DataAsync(HttpStatusCode.Created);

        Assert.Equal(1, transfer.GetProperty("transfer_no").GetInt64());
        var journal = await JournalAsync(setup, transfer);
        Assert.Equal("bank_transfer", journal.GetProperty("source_kind").GetString());
        var lines = journal.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(("500.00", "2.00", "502.00"), (lines[0].GetProperty("debit").GetString(), lines[1].GetProperty("debit").GetString(), lines[2].GetProperty("credit").GetString()));
        Assert.Equal((setup.Savings, setup.Fees, setup.Books.Cash), (lines[0].GetProperty("account_id").GetGuid(), lines[1].GetProperty("account_id").GetGuid(), lines[2].GetProperty("account_id").GetGuid()));
    }

    [Fact]
    public async Task A_transfer_that_cannot_be_posted_is_kept_as_a_draft_with_the_reason_and_posts_once_fixed()
    {
        var setup = await SetUpAsync();
        await new JournalEditTests(host).CloseThroughAsync(setup.Books, "2026-10-31");

        var draft = await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "500.00"))).DataAsync(HttpStatusCode.UnprocessableEntity);
        await using (var db = host.CreateDbContext())
        {
            Assert.Equal(0, await db.Journals.CountAsync(j => j.LedgerId == setup.Books.LedgerId, Ct));
        }
        var fixedTransfer = await (await setup.Books.Admin.WriteAsync(HttpMethod.Put, $"{setup.Url}/{draft.Id()}", Body(setup, "500.00", date: "2026-11-02"), ifMatch: 1)).DataAsync();

        Assert.Equal("draft", draft.GetProperty("status").GetString());
        Assert.Equal("DATE_CLOSED", draft.GetProperty("issues")[0].GetProperty("code").GetString());
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("journal_id").ValueKind);
        Assert.Equal("posted", fixedTransfer.GetProperty("status").GetString());
        Assert.Equal(0, fixedTransfer.GetProperty("issues").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, fixedTransfer.GetProperty("journal_id").ValueKind);
    }

    [Fact]
    public async Task A_failed_change_to_a_posted_transfer_leaves_it_and_its_posting_unchanged()
    {
        var setup = await SetUpAsync();
        var transfer = await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "500.00"))).DataAsync(HttpStatusCode.Created);
        await new JournalEditTests(host).CloseThroughAsync(setup.Books, "2026-10-15");

        var change = await setup.Books.Admin.WriteAsync(HttpMethod.Put, $"{setup.Url}/{transfer.Id()}", Body(setup, "900.00", date: "2026-11-02"), ifMatch: 1);
        var backToDraft = await setup.Books.Admin.WriteAsync(HttpMethod.Put, $"{setup.Url}/{transfer.Id()}", Body(setup, "500.00", post: false), ifMatch: 1);

        await change.AssertProblemAsync(HttpStatusCode.UnprocessableEntity, "DOCUMENT_INVALID");
        await backToDraft.AssertProblemAsync(HttpStatusCode.Conflict, "DOCUMENT_POSTED");
        var listed = (await (await setup.Books.Admin.GetAsync(setup.Url, Ct)).DataAsync()).EnumerateArray().Single();
        Assert.Equal(("500.00", "posted", "1"), (listed.GetProperty("amount").GetString(), listed.GetProperty("status").GetString(), listed.GetProperty("version").GetString()));
        Assert.Equal("500.00", (await JournalAsync(setup, transfer)).GetProperty("lines")[0].GetProperty("debit").GetString());
    }

    [Fact]
    public async Task A_transfer_saved_as_a_draft_on_purpose_posts_nothing_until_saved_and_can_be_deleted()
    {
        var setup = await SetUpAsync();

        var draft = await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "40.00", post: false))).DataAsync(HttpStatusCode.Created);
        await (await setup.Books.Admin.WriteAsync(HttpMethod.Delete, $"{setup.Url}/{draft.Id()}", null, ifMatch: 1)).DataAsync();

        Assert.Equal(("draft", 0), (draft.GetProperty("status").GetString(), draft.GetProperty("issues").GetArrayLength()));
        await using var db = host.CreateDbContext();
        Assert.Equal(0, await db.Journals.CountAsync(j => j.LedgerId == setup.Books.LedgerId, Ct));
    }

    [Fact]
    public async Task The_generated_journal_changes_only_through_the_transfer()
    {
        var setup = await SetUpAsync();
        var transfer = await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "500.00"))).DataAsync(HttpStatusCode.Created);
        var journalUrl = $"{setup.Books.JournalsUrl}/{transfer.GetProperty("journal_id").GetGuid()}";

        var directEdit = await setup.Books.Admin.WriteAsync(HttpMethod.Put, journalUrl, JournalBooks.Body(setup.Books, true, ("1.00", null), (null, "1.00")), ifMatch: 1);
        var directDelete = await setup.Books.Admin.WriteAsync(HttpMethod.Delete, journalUrl, null, ifMatch: 1);
        var edited = await (await setup.Books.Admin.WriteAsync(HttpMethod.Put, $"{setup.Url}/{transfer.Id()}", Body(setup, "650.00"), ifMatch: 1)).DataAsync();

        await directEdit.AssertProblemAsync(HttpStatusCode.Conflict, "JOURNAL_FROM_DOCUMENT");
        await directDelete.AssertProblemAsync(HttpStatusCode.Conflict, "JOURNAL_FROM_DOCUMENT");
        Assert.Equal(transfer.GetProperty("journal_id").GetGuid(), edited.GetProperty("journal_id").GetGuid());
        var journal = await JournalAsync(setup, edited);
        Assert.Equal("650.00", journal.GetProperty("lines")[0].GetProperty("debit").GetString());
        Assert.Equal(2, journal.GetProperty("revision_no").GetInt32());
    }

    [Fact]
    public async Task Deleting_a_transfer_removes_its_posting_and_both_sides_must_be_bank_accounts()
    {
        var setup = await SetUpAsync();
        var transfer = await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "500.00"))).DataAsync(HttpStatusCode.Created);

        await (await setup.Books.Admin.WriteAsync(HttpMethod.Delete, $"{setup.Url}/{transfer.Id()}", null, ifMatch: 1)).DataAsync();
        var toIncome = await setup.Books.Admin.PostAsync(setup.Url, new { date = "2026-10-15", from_account_id = setup.Books.Cash, to_account_id = setup.Books.Sales, amount = "1.00" });

        await using var db = host.CreateDbContext();
        Assert.Null((await db.Journals.SingleAsync(j => j.Id == transfer.GetProperty("journal_id").GetGuid(), Ct)).EffectiveRevisionId);
        await toIncome.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task Transfer_numbers_count_per_ledger_and_ai_clients_cannot_post_transfers()
    {
        var setup = await SetUpAsync();
        await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "1.00"))).DataAsync(HttpStatusCode.Created);
        var second = await (await setup.Books.Admin.PostAsync(setup.Url, Body(setup, "2.00"))).DataAsync(HttpStatusCode.Created);
        using var agent = host.WithToken(host.Issuer.CreateToken(Ids.Subject("agent"), TestHost.McpAudience));

        Assert.Equal(2, second.GetProperty("transfer_no").GetInt64());
        // MCP tokens cannot call the HTTP API at all; the service also refuses agents, covered by the MCP journal tests.
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.PostAsync(setup.Url, Body(setup, "1.00"))).StatusCode);
    }

    private sealed record Setup(JournalBooks Books, Guid Savings, Guid Fees, string Url);

    private async Task<Setup> SetUpAsync()
    {
        var books = await JournalBooks.CreateAsync(host);
        var savings = (await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "1100", "Savings", "assets")).Id();
        var fees = (await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "5900", "Bank fees", "cost_and_expense")).Id();
        var banks = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/bank-accounts";
        await (await books.Admin.PostAsync(banks, new { account_id = books.Cash, bank_name = "ABA" })).DataAsync(HttpStatusCode.Created);
        await (await books.Admin.PostAsync(banks, new { account_id = savings, bank_name = "ACLEDA" })).DataAsync(HttpStatusCode.Created);
        return new Setup(books, savings, fees, $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/transfers");
    }

    private static object Body(Setup setup, string amount, string? fee = null, string date = "2026-10-15", bool post = true) => new
    {
        date,
        post,
        from_account_id = setup.Books.Cash,
        to_account_id = setup.Savings,
        amount,
        fee_amount = fee,
        fee_account_id = fee is null ? (Guid?)null : setup.Fees,
        reference = "TT-001",
    };

    private static async Task<JsonElement> JournalAsync(Setup setup, JsonElement transfer) =>
        await (await setup.Books.Admin.GetAsync($"{setup.Books.JournalsUrl}/{transfer.GetProperty("journal_id").GetGuid()}", Ct)).DataAsync();
}
