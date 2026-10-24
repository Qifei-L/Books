using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;

namespace Books.Tests.Documents;

/// <summary>The tax pack's monthly return definition and the return categories of withholding codes (owner decisions, M8).</summary>
public sealed class TaxPackTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_cambodia_pack_lists_its_boxes_and_the_template_categorizes_withholding()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();

        var pack = await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-pack", Ct)).DataAsync();
        var codes = (await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-codes", Ct)).DataAsync()).EnumerateArray()
            .ToDictionary(c => c.GetProperty("name").GetString()!, c => c.GetProperty("return_category"));

        Assert.Equal(("cambodia-2026", "KHR"), (pack.GetProperty("tax_pack").GetString(), pack.GetProperty("tax_currency").GetString()));
        var boxes = pack.GetProperty("boxes").EnumerateArray().Select(b => b.GetProperty("key").GetString()).ToList();
        Assert.Contains("output_vat", boxes);
        Assert.Contains("prepayment", boxes);
        Assert.Contains("wht_rent", boxes);
        Assert.Equal(["services", "rent", "non_resident"], pack.GetProperty("withholding_categories").EnumerateArray().Select(c => c.GetProperty("key").GetString()));
        Assert.Equal("rent", codes["WHT 10% rent"].GetString());
        Assert.Equal(JsonValueKind.Null, codes["VAT 10% (sales)"].ValueKind);
    }

    [Fact]
    public async Task Only_withholding_codes_take_a_category_from_the_pack()
    {
        var s = await new PurchaseInvoiceTests(host).SetUpAsync();
        var url = $"{s.LedgerUrl}/tax-codes";

        var onVat = await s.Books.Admin.PostAsync(url, new { name = "VAT 10% B", kind = "vat_output", rate = "10", account_id = s.Account("2100"), return_category = "rent" });
        var unknown = await s.Books.Admin.PostAsync(url, new { name = "WHT 5%", kind = "withholding", rate = "5", account_id = s.Account("2200"), return_category = "dividends" });
        var created = await (await s.Books.Admin.PostAsync(url, new { name = "WHT 15% B", kind = "withholding", rate = "15", account_id = s.Account("2200"), return_category = "services" }))
            .DataAsync(HttpStatusCode.Created);

        await onVat.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        await unknown.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
        Assert.Equal("services", created.GetProperty("return_category").GetString());
    }

    [Fact]
    public async Task A_ledger_without_a_tax_pack_has_no_return()
    {
        var books = await JournalBooks.CreateAsync(host);

        var response = await books.Admin.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/tax-pack", Ct);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, "NO_TAX_PACK");
    }
}
