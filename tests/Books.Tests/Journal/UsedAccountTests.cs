using System.Net;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Journal;

/// <summary>Rules 17 and 19: used accounts keep their type and cannot be deleted; system purposes.</summary>
public sealed class UsedAccountTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_used_even_by_a_draft_cannot_be_deleted_or_change_type_but_can_be_deactivated()
    {
        var books = await JournalBooks.CreateAsync(host);
        await (await books.SaveAsync(false, ("1.00", null))).DataAsync(HttpStatusCode.Created);
        var url = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/accounts/{books.Cash}";

        var delete = await books.Admin.WriteAsync(HttpMethod.Delete, url, null, ifMatch: 1);
        var retype = await books.Admin.WriteAsync(HttpMethod.Patch, url, new { type = "liabilities" }, ifMatch: 1);
        var deactivate = await (await books.Admin.WriteAsync(HttpMethod.Patch, url, new { active = false }, ifMatch: 1)).DataAsync();

        await delete.AssertProblemAsync(HttpStatusCode.Conflict, "ACCOUNT_IN_USE");
        await retype.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.True(deactivate.GetProperty("used").GetBoolean());
        Assert.False(deactivate.GetProperty("postable").GetBoolean());
    }

    [Fact]
    public async Task Retained_earnings_must_be_an_equity_account_and_keeps_its_type()
    {
        var books = await JournalBooks.CreateAsync(host);
        var equity = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "3000", "Retained earnings", "equity");
        var purposes = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/purposes";

        var onCash = await books.Admin.WriteAsync(HttpMethod.Put, $"{purposes}/retained_earnings", new { account_id = books.Cash }, ifMatch: 1);
        var assigned = await (await books.Admin.WriteAsync(HttpMethod.Put, $"{purposes}/retained_earnings", new { account_id = equity.Id() }, ifMatch: 1)).DataAsync();
        var account = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/accounts/{equity.Id()}";
        var retype = await books.Admin.WriteAsync(HttpMethod.Patch, account, new { type = "assets" }, ifMatch: 1);
        var delete = await books.Admin.WriteAsync(HttpMethod.Delete, account, null, ifMatch: 1);

        await onCash.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Contains(assigned.EnumerateArray(), p => p.GetProperty("purpose").GetString() == "retained_earnings" && p.GetProperty("account_id").GetGuid() == equity.Id());
        await retype.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await delete.AssertProblemAsync(HttpStatusCode.Conflict, "ACCOUNT_HAS_PURPOSE");
    }

    [Fact]
    public async Task The_trial_balance_warns_while_opening_balance_clearing_is_not_zero()
    {
        var books = await JournalBooks.CreateAsync(host);
        var clearing = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "3999", "Opening balance clearing", "equity");
        var bank = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "1100", "Bank", "assets");
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/purposes/opening_balance_clearing",
            new { account_id = clearing.Id() }, ifMatch: 1)).DataAsync();
        await PostAsync(books, books.Cash, clearing.Id(), "500.00");
        var tbUrl = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/trial-balance?from=2026-01-01&to=2026-12-31";

        var halfway = await (await books.Admin.GetAsync(tbUrl, Ct)).DataAsync();
        await PostAsync(books, clearing.Id(), bank.Id(), "500.00");
        var done = await (await books.Admin.GetAsync(tbUrl, Ct)).DataAsync();

        Assert.Equal("OPENING_BALANCE_CLEARING_NOT_ZERO", Assert.Single(halfway.GetProperty("warnings").EnumerateArray()).GetProperty("code").GetString());
        Assert.Equal(0, done.GetProperty("warnings").GetArrayLength());
    }

    private static async Task PostAsync(JournalBooks books, Guid debit, Guid credit, string amount) =>
        await (await books.Admin.PostAsync(books.JournalsUrl, new
        {
            accounting_date = "2026-01-01",
            post = true,
            lines = new object[] { new { account_id = debit, debit = amount }, new { account_id = credit, credit = amount } },
        })).DataAsync(HttpStatusCode.Created);
}
