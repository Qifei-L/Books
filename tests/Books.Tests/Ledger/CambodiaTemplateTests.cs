using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Ledger;

/// <summary>Owner decision: a versioned Cambodia starter template with accounts, usages, system purposes and tax codes.</summary>
public sealed class CambodiaTemplateTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Applying_the_template_sets_up_accounts_usages_purposes_and_tax_codes()
    {
        var books = await JournalBooks.CreateAsync(host);

        var summary = await (await ApplyAsync(books)).DataAsync();

        Assert.Equal(22, summary.GetProperty("created").GetArrayLength());
        var accounts = (await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray().ToDictionary(a => a.GetProperty("code").GetString()!);
        Assert.Equal(["payable"], accounts["2000"].GetProperty("usages").EnumerateArray().Select(u => u.GetString()));
        Assert.True(accounts["2000"].GetProperty("is_control").GetBoolean());
        Assert.Equal(["suspense"], accounts["2999"].GetProperty("purposes").EnumerateArray().Select(u => u.GetString()));
        var taxCodes = await GetAsync(books, "tax-codes");
        var purchasesVat = taxCodes.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "VAT 10% (purchases)");
        Assert.Equal(accounts["5900"].Id(), purchasesVat.GetProperty("non_deductible_account_id").GetGuid());
        Assert.Equal(8, taxCodes.GetArrayLength());
        Assert.Equal("cambodia-2026", (await GetAsync(books, "")).GetProperty("tax_pack").GetString());
    }

    [Fact]
    public async Task Applying_it_again_creates_nothing_and_existing_accounts_are_reused()
    {
        var books = await JournalBooks.CreateAsync(host);
        var myPayable = await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "2010", "Trade creditors", "liabilities");
        await (await books.Admin.WriteAsync(HttpMethod.Put, $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/accounts/{myPayable.Id()}/usages",
            new { usages = new[] { "payable" } }, ifMatch: 1)).DataAsync();

        var first = await (await ApplyAsync(books)).DataAsync();
        var second = await (await ApplyAsync(books)).DataAsync();

        Assert.Contains(first.GetProperty("reused").EnumerateArray(), r => r.GetString() == "account 2010 Trade creditors");
        Assert.Equal(0, second.GetProperty("created").GetArrayLength());
        var codes = (await AccountTests.ListAsync(books.Admin, books.OrgId, books.LedgerId)).EnumerateArray().Select(a => a.GetProperty("code").GetString()).ToList();
        Assert.DoesNotContain("2000", codes);
    }

    [Fact]
    public async Task A_code_used_by_another_type_stops_the_template_and_nothing_is_applied()
    {
        var books = await JournalBooks.CreateAsync(host);
        await AccountTests.CreateAsync(books.Admin, books.OrgId, books.LedgerId, "1300", "Prepaid rent", "liabilities");

        var response = await ApplyAsync(books);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "TEMPLATE_CONFLICT");
        Assert.Equal(0, (await GetAsync(books, "tax-codes")).GetArrayLength());
    }

    [Fact]
    public async Task Only_administrators_apply_templates()
    {
        var books = await JournalBooks.CreateAsync(host);
        using var editor = await books.AddMemberAsync("edit");

        var response = await editor.PostAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/templates/cambodia-2026", new { });

        await response.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    private static Task<HttpResponseMessage> ApplyAsync(JournalBooks books) =>
        books.Admin.PostAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/templates/cambodia-2026", new { });

    private static async Task<JsonElement> GetAsync(JournalBooks books, string resource) =>
        await (await books.Admin.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/{resource}".TrimEnd('/'), Ct)).DataAsync();
}
