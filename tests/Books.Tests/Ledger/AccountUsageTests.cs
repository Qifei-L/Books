using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Ledger;

/// <summary>Account Usage (owner decisions): system-defined usages with constraints; every account mapping follows it.</summary>
public sealed class AccountUsageTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tax_codes_and_bank_accounts_give_their_accounts_the_matching_usage()
    {
        var books = await JournalBooks.CreateAsync(host);
        var vatIn = await Account(books, "1300", "VAT receivable", "assets");
        var nonDeductible = await Account(books, "5400", "Non-deductible VAT", "cost_and_expense");

        var noNonDeductible = await books.Admin.PostAsync(Url(books, "tax-codes"), new { name = "VAT in", kind = "vat_input", rate = "10", account_id = vatIn });
        await (await books.Admin.PostAsync(Url(books, "tax-codes"), new { name = "VAT in", kind = "vat_input", rate = "10", account_id = vatIn, non_deductible_account_id = nonDeductible }))
            .DataAsync(HttpStatusCode.Created);
        await (await books.Admin.PostAsync(Url(books, "bank-accounts"), new { account_id = books.Cash, bank_name = "ABA" })).DataAsync(HttpStatusCode.Created);

        await noNonDeductible.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        var usages = await UsagesAsync(books);
        Assert.Equal(["input_vat"], usages[vatIn]);
        Assert.Equal(["non_deductible_vat"], usages[nonDeductible]);
        Assert.Equal(["bank"], usages[books.Cash]);
    }

    [Fact]
    public async Task Payable_usage_needs_a_liability_and_makes_an_unused_account_a_control_account()
    {
        var books = await JournalBooks.CreateAsync(host);
        var payable = await Account(books, "2000", "Accounts payable", "liabilities");

        var onAsset = await SetUsagesAsync(books, books.Cash, ["payable"]);
        var set = await (await SetUsagesAsync(books, payable, ["payable"])).DataAsync();

        await onAsset.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.True(set.GetProperty("is_control").GetBoolean());
        var account = (await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray().Single(a => a.Id() == payable);
        Assert.True(account.GetProperty("is_control").GetBoolean());
    }

    [Fact]
    public async Task An_account_already_used_by_journals_cannot_become_payable()
    {
        var books = await JournalBooks.CreateAsync(host);
        var loan = await Account(books, "2500", "Loan", "liabilities");
        await (await books.Admin.PostAsync(books.JournalsUrl, new
        {
            accounting_date = "2026-10-15",
            post = true,
            lines = new object[] { new { account_id = books.Cash, debit = "10.00" }, new { account_id = loan, credit = "10.00" } },
        })).DataAsync(HttpStatusCode.Created);

        var response = await SetUsagesAsync(books, loan, ["payable"]);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_usage_cannot_be_removed_while_bank_details_rely_on_it()
    {
        var books = await JournalBooks.CreateAsync(host);
        await (await books.Admin.PostAsync(Url(books, "bank-accounts"), new { account_id = books.Cash, bank_name = "ABA" })).DataAsync(HttpStatusCode.Created);
        var version = long.Parse((await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray()
            .Single(a => a.Id() == books.Cash).GetProperty("version").GetString()!);

        var response = await books.Admin.WriteAsync(HttpMethod.Put, $"{Url(books, "accounts")}/{books.Cash}/usages", new { usages = Array.Empty<string>() }, ifMatch: version);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "USAGE_IN_USE");
    }

    private static string Url(JournalBooks books, string resource) => $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/{resource}";

    private static async Task<Guid> Account(JournalBooks books, string code, string name, string type) =>
        (await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, code, name, type)).Id();

    private static Task<HttpResponseMessage> SetUsagesAsync(JournalBooks books, Guid account, string[] usages) =>
        books.Admin.WriteAsync(HttpMethod.Put, $"{Url(books, "accounts")}/{account}/usages", new { usages }, ifMatch: 1);

    private static async Task<Dictionary<Guid, string[]>> UsagesAsync(JournalBooks books) =>
        (await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray()
            .ToDictionary(a => a.Id(), a => a.GetProperty("usages").EnumerateArray().Select(u => u.GetString()!).ToArray());
}
