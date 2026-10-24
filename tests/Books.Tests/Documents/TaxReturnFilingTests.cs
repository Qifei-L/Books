using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// Filing a month (owner decision): the return is kept as filed and the ledger is closed through the month end;
/// a reopened and changed month shows its differences from the filed return.
/// </summary>
public sealed class TaxReturnFilingTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filing_keeps_the_return_and_closes_the_month()
    {
        var s = await new TaxReturnTests(host).SeedAsync();

        var filed = await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-returns/2026-10/file", new { })).DataAsync(HttpStatusCode.Created);
        var again = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-returns/2026-10/file", new { });
        var late = await (await s.Books.Admin.PostAsync(s.Url, SalesInvoiceTests.Body(s, date: "2026-10-30"))).DataAsync(HttpStatusCode.UnprocessableEntity);
        var list = await TaxReturnTests.GetAsync(s, "tax-returns");
        var october = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10");

        Assert.Equal("-10.00", TaxReturnTests.Boxes(filed)["net_vat"].Usd);
        await again.AssertProblemAsync(HttpStatusCode.Conflict, "RETURN_ALREADY_FILED");
        Assert.Equal("DATE_CLOSED", late.GetProperty("issues")[0].GetProperty("code").GetString());
        Assert.Equal("2026-10", Assert.Single(list.EnumerateArray()).GetProperty("period").GetString());
        Assert.Empty(october.GetProperty("filed").GetProperty("differences").EnumerateArray());
        Assert.Equal("2026-10-31", (await LedgerAsync(s)).GetProperty("closed_through").GetString());
    }

    [Fact]
    public async Task A_reopened_and_changed_month_shows_what_differs_from_the_filed_return()
    {
        var s = await new TaxReturnTests(host).SeedAsync();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-returns/2026-10/file", new { })).DataAsync(HttpStatusCode.Created);
        var version = long.Parse((await LedgerAsync(s)).GetProperty("version").GetString()!);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Put, $"{s.LedgerUrl}/close", new { closed_through = "2026-09-30" }, ifMatch: version)).DataAsync();
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-30",
            lines = new[] { new { account_id = s.Books.Sales, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);

        var october = await TaxReturnTests.GetAsync(s, "tax-returns/2026-10");

        var differences = october.GetProperty("filed").GetProperty("differences").EnumerateArray().ToDictionary(d => d.GetProperty("key").GetString()!);
        Assert.Equal(("90.00", "100.00"), (differences["output_vat"].GetProperty("filed_amount").GetString(), differences["output_vat"].GetProperty("current_amount").GetString()));
        Assert.False(differences.ContainsKey("input_vat"));
    }

    [Fact]
    public async Task Only_administrators_file_and_an_incomplete_return_cannot_be_filed()
    {
        var s = await new TaxReturnTests(host).SeedAsync();
        using var editor = await s.Books.AddMemberAsync("edit");
        var wht = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-codes", new { name = "WHT 15% other", kind = "withholding", rate = "15", account_id = s.Account("2200") }))
            .DataAsync(HttpStatusCode.Created)).Id();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Consultant", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        var fees = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "5100", "Fees", "cost_and_expense")).Id();
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-10", lines = new[] { new { account_id = fees, amount = "200.00", withholding_code_id = wht } },
        })).DataAsync(HttpStatusCode.Created);

        var byEditor = await editor.PostAsync($"{s.LedgerUrl}/tax-returns/2026-10/file", new { });
        var incomplete = await s.Books.Admin.PostAsync($"{s.LedgerUrl}/tax-returns/2026-10/file", new { });

        await byEditor.AssertProblemAsync(HttpStatusCode.Forbidden, "FORBIDDEN");
        await incomplete.AssertProblemAsync((HttpStatusCode)422, "RETURN_INCOMPLETE");
        Assert.Empty((await TaxReturnTests.GetAsync(s, "tax-returns")).EnumerateArray());
    }

    private static async Task<JsonElement> LedgerAsync(SalesInvoiceTests.Setup s) =>
        await (await s.Books.Admin.GetAsync(s.LedgerUrl, Ct)).DataAsync();
}
