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
        Assert.Empty(october.GetProperty("issues").EnumerateArray());
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

    /// <summary>
    /// October: sales invoice (goods 1000 at VAT 10%, export service 250 at 0%), credit note 100 + VAT 10,
    /// purchase invoice (rent 1000 at VAT 10% with a valid invoice and WHT 10%; supplies 50 without a valid invoice),
    /// a draft sale that must not count. KHR rate 4100 from the setup.
    /// </summary>
    internal async Task<SalesInvoiceTests.Setup> SeedAsync()
    {
        var s = await new SalesInvoiceTests(host).SetUpAsync();
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

    internal static Dictionary<string, (string Usd, string? Khr)> Boxes(JsonElement taxReturn) =>
        taxReturn.GetProperty("boxes").EnumerateArray().ToDictionary(b => Get(b, "key"), b => (Get(b, "amount"), b.GetProperty("tax_currency_amount").GetString()));

    internal static async Task<JsonElement> GetAsync(SalesInvoiceTests.Setup s, string path) =>
        await (await s.Books.Admin.GetAsync($"{s.LedgerUrl}/{path}", Ct)).DataAsync();

    private static string Get(JsonElement e, string property) => e.GetProperty(property).GetString()!;
}
