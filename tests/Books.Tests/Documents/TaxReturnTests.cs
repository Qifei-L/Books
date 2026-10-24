using System.Net;
using System.Text.Json;
using Books.Tests.Infrastructure;
using Books.Tests.Ledger;

namespace Books.Tests.Documents;

/// <summary>
/// The monthly return (M8): boxes from posted documents dated in the month, USD and KHR from the documents'
/// snapshots or one overridden rate, drilldown to lines, prepayment at 1% and withholding by category.
/// </summary>
public sealed class TaxReturnTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task October_adds_up_sales_purchases_prepayment_and_withholding()
    {
        var s = await SeedAsync();

        var october = await GetAsync(s, "tax-returns/2026-10");

        var boxes = Boxes(october);
        Assert.Equal(("900.00", "90.00", "250.00", "0.00"), (boxes["taxable_sales"].Usd, boxes["output_vat"].Usd, boxes["zero_rated_sales"].Usd, boxes["non_vat_sales"].Usd));
        Assert.Equal(("1000.00", "100.00", "-10.00", "5.00"), (boxes["taxable_purchases"].Usd, boxes["input_vat"].Usd, boxes["net_vat"].Usd, boxes["non_deductible_vat"].Usd));
        Assert.Equal(("1150.00", "11.50"), (boxes["turnover"].Usd, boxes["prepayment"].Usd));
        Assert.Equal(("1000.00", "100.00", "0.00", "100.00"), (boxes["wht_rent_base"].Usd, boxes["wht_rent"].Usd, boxes["wht_services"].Usd, boxes["wht_total"].Usd));
        Assert.Equal(("369000", "410000", "-41000", "47150"), (boxes["output_vat"].Khr, boxes["input_vat"].Khr, boxes["net_vat"].Khr, boxes["prepayment"].Khr));
        Assert.Equal("document_snapshots", october.GetProperty("tax_currency_basis").GetString());
        // The October draft is not in the return; it is flagged so it can be posted or re-dated before filing.
        Assert.Equal(["DRAFTS_IN_PERIOD"], october.GetProperty("issues").EnumerateArray().Select(i => i.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task A_box_drills_down_to_its_document_lines()
    {
        var s = await SeedAsync();

        var lines = await GetAsync(s, "tax-returns/2026-10/boxes/output_vat");

        Assert.Equal([("sales_invoice", "100.00", "410000"), ("sales_credit_note", "-10.00", "-41000")],
            lines.GetProperty("lines").EnumerateArray().Select(l => (Get(l, "document_type"), Get(l, "amount"), Get(l, "tax_currency_amount"))));
    }

    [Fact]
    public async Task One_rate_for_the_month_needs_a_reason()
    {
        var s = await SeedAsync();

        var overridden = await GetAsync(s, "tax-returns/2026-10?khrRate=4200&khrReason=NBC%2031%20Oct%20rate");
        var noReason = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-10?khrRate=4200", Ct);

        Assert.Equal(("override", "4200", "378000"), (overridden.GetProperty("tax_currency_basis").GetString(), overridden.GetProperty("override_rate").GetString(), Boxes(overridden)["output_vat"].Khr));
        await noReason.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task Other_months_drafts_and_bad_periods_stay_out()
    {
        var s = await SeedAsync();

        var september = await GetAsync(s, "tax-returns/2026-09");
        var badPeriod = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-13", Ct);

        Assert.All(Boxes(september).Values, b => Assert.Equal(0m, decimal.Parse(b.Usd)));
        await badPeriod.AssertProblemAsync(HttpStatusCode.BadRequest, "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task A_manual_vat_correction_counts_in_the_return_and_opens_from_its_box()
    {
        var s = await SeedAsync();
        var matched = await GetAsync(s, "tax-returns/2026-10");
        await (await s.Books.Admin.PostAsync(s.Books.JournalsUrl, new
        {
            accounting_date = "2026-10-31", post = true, reference = "ADJ-1", description = "VAT correction",
            lines = new object[] { new { account_id = s.Books.Cash, debit = "5.00" }, new { account_id = s.Account("2100"), credit = "5.00" } },
        })).DataAsync(HttpStatusCode.Created);

        var october = await GetAsync(s, "tax-returns/2026-10");
        var drilldown = await GetAsync(s, "tax-returns/2026-10/boxes/output_vat");

        Assert.All(matched.GetProperty("reconciliation").EnumerateArray(), r => Assert.True(r.GetProperty("matches").GetBoolean()));
        Assert.Equal(("95.00", "389500"), Boxes(october)["output_vat"]);           // 90.00 from documents + 5.00 journal, at 4100
        Assert.Equal("-5.00", Boxes(october)["net_vat"].Usd);
        var vat = october.GetProperty("reconciliation").EnumerateArray().Single(r => Get(r, "usage") == "output_vat");
        Assert.Equal(("95.00", "95.00", true), (Get(vat, "ledger_amount"), Get(vat, "return_amount"), vat.GetProperty("matches").GetBoolean()));
        var other = Assert.Single(vat.GetProperty("other_postings").EnumerateArray());
        Assert.Equal(("ADJ-1", "journal", "5.00"), (Get(other, "reference"), Get(other, "source"), Get(other, "amount")));
        var journal = drilldown.GetProperty("lines").EnumerateArray().Single(l => Get(l, "document_type") == "journal");
        Assert.Equal(("ADJ-1", "5.00", "4100"), (Get(journal, "reference"), Get(journal, "amount"), Get(journal, "tax_currency_rate")));
    }

    [Fact]
    public async Task Lines_with_an_overridden_vat_are_listed_for_review()
    {
        var s = await SeedAsync();
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-26",
            lines = new[] { new { account_id = s.Books.Sales, amount = "33.33", tax_code_id = s.Taxes["VAT 10% (sales)"], tax_amount = "3.34", tax_reason = "Customer PO rounding" } },
        })).DataAsync(HttpStatusCode.Created);

        var october = await GetAsync(s, "tax-returns/2026-10");

        var adjusted = Assert.Single(october.GetProperty("adjusted_lines").EnumerateArray());
        Assert.Equal(("3.33", "3.34", "Customer PO rounding"), (Get(adjusted, "tax_calculated"), Get(adjusted, "tax_amount"), Get(adjusted, "tax_reason")));
    }

    /// <summary>
    /// October: sales invoice (goods 1000 at VAT 10%, export service 250 at 0%), credit note 100 + VAT 10,
    /// purchase invoice (rent 1000 at VAT 10% with a valid invoice and WHT 10%; supplies 50 without a valid invoice),
    /// a draft sale that must not count. KHR rate 4100 from the setup.
    /// </summary>
    internal async Task<SalesInvoiceTests.Setup> SeedAsync(string? subject = null)
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync(subject);
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Landlord", is_supplier = true })).DataAsync(HttpStatusCode.Created)).Id();
        var rent = (await AccountTests.CreateAsync(s.Books.Admin, s.Books.OrgId, s.Books.LedgerId, "5000", "Rent", "cost_and_expense")).Id();
        await (await s.Books.Admin.PostAsync(s.Url, SalesInvoiceTests.Body(s))).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/credit-notes", new
        {
            customer_id = s.Customer, date = "2026-10-20",
            lines = new[] { new { account_id = s.Books.Sales, amount = "100.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-01",
            lines = new object[]
            {
                new { account_id = rent, amount = "1000.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = true, withholding_code_id = s.Taxes["WHT 10% rent"] },
                new { account_id = rent, amount = "50.00", tax_code_id = s.Taxes["VAT 10% (purchases)"], valid_tax_invoice = false },
            },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-25", post = false,
            lines = new[] { new { account_id = s.Books.Sales, amount = "999.00", tax_code_id = s.Taxes["VAT 10% (sales)"] } },
        })).DataAsync(HttpStatusCode.Created);
        return s;
    }

    /// <summary>Deletes the month's drafts, which would otherwise block filing.</summary>
    internal static async Task DeleteDraftsAsync(SalesInvoiceTests.Setup s)
    {
        foreach (var draft in (await GetAsync(s, "sales-invoices")).EnumerateArray().Where(d => Get(d, "status") == "draft"))
        {
            await (await s.Books.Admin.WriteAsync(HttpMethod.Delete, $"{s.Url}/{draft.Id()}", null, ifMatch: long.Parse(Get(draft, "version")))).DataAsync();
        }
    }

    internal static Dictionary<string, (string Usd, string? Khr)> Boxes(JsonElement taxReturn) =>
        taxReturn.GetProperty("boxes").EnumerateArray().ToDictionary(b => Get(b, "key"), b => (Get(b, "amount"), b.GetProperty("tax_currency_amount").GetString()));

    internal static async Task<JsonElement> GetAsync(SalesInvoiceTests.Setup s, string path) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/{path}", Ct)).DataAsync();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
