using System.Net;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Ledger;

/// <summary>Bank accounts (one per Assets account) and hand-entered exchange rates.</summary>
public sealed class BankTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bank_account_attaches_to_one_assets_account_and_marks_it_as_bank()
    {
        var books = await JournalBooks.CreateAsync(host);
        var url = $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/bank-accounts";

        var created = await (await books.Admin.PostAsync(url, new { account_id = books.Cash, bank_name = "ABA Bank", account_number = "000 123 456" })).DataAsync(HttpStatusCode.Created);
        var again = await books.Admin.PostAsync(url, new { account_id = books.Cash, bank_name = "ACLEDA" });
        var onIncome = await books.Admin.PostAsync(url, new { account_id = books.Sales, bank_name = "ACLEDA" });
        var renamed = await (await books.Admin.WriteAsync(HttpMethod.Put, $"{url}/{books.Cash}", new { bank_name = "ABA Bank PLC", account_number = "000 123 456" }, ifMatch: 1)).DataAsync();

        Assert.Equal("USD", created.GetProperty("currency").GetString());
        await again.AssertProblemAsync(HttpStatusCode.Conflict, "BANK_ACCOUNT_EXISTS");
        await onIncome.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal("ABA Bank PLC", renamed.GetProperty("bank_name").GetString());
        var cash = (await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray().Single(a => a.Id() == books.Cash);
        Assert.True(cash.GetProperty("is_bank").GetBoolean());
    }

    [Fact]
    public async Task Administrators_set_daily_rates_and_a_second_entry_replaces_the_first()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var editor = await books.AddMemberAsync("edit");
        var url = $"/api/v1/organizations/{books.OrgId}/exchange-rates";

        await (await books.Admin.WriteAsync(HttpMethod.Put, url, new { date = "2026-10-31", from_currency = "USD", to_currency = "KHR", rate = "4100", source = "NBC" })).DataAsync();
        var replaced = await (await books.Admin.WriteAsync(HttpMethod.Put, url, new { date = "2026-10-31", from_currency = "USD", to_currency = "KHR", rate = "4105.5" })).DataAsync();
        var byEditor = await editor.WriteAsync(HttpMethod.Put, url, new { date = "2026-10-30", from_currency = "USD", to_currency = "KHR", rate = "4100" });
        var sameCurrency = await books.Admin.WriteAsync(HttpMethod.Put, url, new { date = "2026-10-30", from_currency = "USD", to_currency = "USD", rate = "1" });
        var listed = await (await editor.GetAsync($"{url}?from=2026-10-01&to=2026-10-31", Ct)).DataAsync();

        Assert.Equal(("4105.5", "Manual", "2"), (replaced.GetProperty("rate").GetString(), replaced.GetProperty("source").GetString(), replaced.GetProperty("version").GetString()));
        await byEditor.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await sameCurrency.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Single(listed.EnumerateArray());
    }
}
