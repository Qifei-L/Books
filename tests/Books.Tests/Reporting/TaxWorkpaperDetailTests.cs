using System.Net;
using Books.Tests.Infrastructure;
using ClosedXML.Excel;

namespace Books.Tests.Reporting;

/// <summary>The tax workpaper carries the two detail sheets: state charge VAT and non-creditable input VAT.</summary>
public sealed class TaxWorkpaperDetailTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task State_charge_and_non_deductible_lines_reach_both_the_json_and_the_workpaper()
    {
        var s = await new Documents.SalesInvoiceTests(host).SetUpAsync();
        var supplier = (await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/parties", new { name = "Paper supplier", is_supplier = true }))
            .DataAsync(HttpStatusCode.Created)).Id();

        await (await s.Books.Admin.PostAsync($"{s.LedgerUrl}/purchase-invoices", new
        {
            supplier_id = supplier, date = "2026-10-05", lines = new object[]
            {
                new { account_id = s.Account("5800"), amount = "100.00", tax_code_id = s.Taxes["VAT 10% (purchases, non-creditable)"] },
            },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.PostAsync(s.Url, new
        {
            customer_id = s.Customer, date = "2026-10-06", lines = new object[]
            {
                new { account_id = s.Services, amount = "100.00", tax_code_id = s.Taxes["State Charge 10% (sales)"] },
            },
        })).DataAsync(HttpStatusCode.Created);

        var taxReturn = await Documents.TaxReturnTests.GetAsync(s, "tax-returns/2026-10");
        var stateCharge = taxReturn.GetProperty("state_charge_lines").EnumerateArray().Single();
        var nonDeductible = taxReturn.GetProperty("non_deductible_lines").EnumerateArray().Single();
        Assert.Equal(("100.00", "10.00"), (stateCharge.GetProperty("net").GetString(), stateCharge.GetProperty("tax").GetString()));
        Assert.Equal("10", nonDeductible.GetProperty("box").GetString());
        var boxes = Documents.TaxReturnTests.Boxes(taxReturn);
        // State charge is ordinary output VAT: it still lands in the normal VAT box.
        Assert.Equal("10.00", boxes["t701_15"].Usd);
        Assert.Equal("10.00", boxes["t701_non_deductible_vat"].Usd);

        var response = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-10/tax-workpaper.xlsx", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        var stateChargeSheet = workbook.Worksheet("State charge");
        var nonDeductibleSheet = workbook.Worksheet("Non-deductible");
        Assert.Contains(stateChargeSheet.CellsUsed(), c => c.GetString().Contains("sales_invoice"));
        Assert.Contains(nonDeductibleSheet.CellsUsed(), c => c.GetString().Contains("purchase_invoice"));
        Assert.Contains(stateChargeSheet.CellsUsed(), c => c.DataType == XLDataType.Number && c.GetDouble() == 100);
        Assert.Contains(nonDeductibleSheet.CellsUsed(), c => c.DataType == XLDataType.Number && c.GetDouble() == 10);

        var pdf = await s.Books.Admin.GetAsync($"{s.LedgerUrl}/tax-returns/2026-10.pdf", Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.True((await pdf.Content.ReadAsByteArrayAsync(Ct)).Length > 1000);
    }
}
