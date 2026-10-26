using System.Net;
using Books.Tests.Infrastructure;
using Books.Tests.Journal;
using ClosedXML.Excel;

namespace Books.Tests.Reporting;

/// <summary>Excel packs are multi-sheet, numeric and follow the published template's section switches.</summary>
public sealed class ExcelPackTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_client_pack_has_one_sheet_per_section_with_numeric_amounts()
    {
        var books = await new StatementTests(host).SeedAsync();

        using var workbook = await DownloadAsync(books.Admin, $"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/client-pack.xlsx?from=2026-01-01&to=2026-12-31",
            "client-pack.xlsx");

        Assert.True(workbook.TryGetWorksheet("Cover", out _));
        Assert.True(workbook.TryGetWorksheet("Profit and Loss", out var pl));
        Assert.True(workbook.TryGetWorksheet("Balance Sheet", out var bs));
        Assert.True(workbook.TryGetWorksheet("Trial Balance", out var tb));
        Assert.True(workbook.TryGetWorksheet("Aging", out _));
        Assert.Equal(XLDataType.Number, pl.CellsUsed().Single(c => c.GetString() == "Net profit").CellRight().DataType);
        Assert.Equal(330m, pl.CellsUsed().Single(c => c.GetString() == "Net profit").CellRight().GetValue<decimal>());
        Assert.Equal(XLDataType.Number, bs.CellsUsed().Single(c => c.GetString() == "Total Assets").CellRight().DataType);
        Assert.True(tb.SheetView.SplitRow >= 4);
    }

    [Fact]
    public async Task The_tax_workpaper_has_boxes_reconciliation_and_issues_sheets()
    {
        var s = await new Books.Tests.Documents.PurchaseInvoiceTests(host).SetUpAsync();

        using var workbook = await DownloadAsync(s.Books.Admin, $"{s.LedgerUrl}/tax-returns/2026-10/tax-workpaper.xlsx", "tax-workpaper.xlsx");

        Assert.True(workbook.TryGetWorksheet("Boxes", out var boxes));
        Assert.True(workbook.TryGetWorksheet("Reconciliation", out _));
        Assert.True(workbook.TryGetWorksheet("State charge", out _));
        Assert.True(workbook.TryGetWorksheet("Non-deductible", out _));
        Assert.True(workbook.TryGetWorksheet("Issues", out _));
        Assert.Contains(boxes.CellsUsed(), c => c.GetString() == "t701_19");
        Assert.Equal(XLDataType.Number, boxes.CellsUsed().Single(c => c.GetString() == "t701_19").CellRight(2).DataType);
    }

    [Fact]
    public async Task A_published_template_decides_which_sheets_the_pack_has()
    {
        var s = await new Books.Tests.Documents.PurchaseInvoiceTests(host).SetUpAsync();
        var url = $"{s.LedgerUrl}/report-templates";
        var draft = await (await s.Books.Admin.PostAsync(url, new
        {
            kind = "client_pack", name = "P&L only", effective_from = "2026-01-01",
            definition = new
            {
                title = "P&L only",
                sections = new object[] { new { key = "cover", enabled = true }, new { key = "pl", enabled = true } },
            },
        })).DataAsync(HttpStatusCode.Created);
        await (await s.Books.Admin.WriteAsync(HttpMethod.Post, $"{url}/{draft.Id()}/publish", null)).DataAsync();

        using var workbook = await DownloadAsync(s.Books.Admin, $"{s.LedgerUrl}/reports/client-pack.xlsx?from=2026-01-01&to=2026-10-31",
            "client-pack.xlsx");

        Assert.True(workbook.TryGetWorksheet("Profit and Loss", out _));
        Assert.False(workbook.TryGetWorksheet("Trial Balance", out _));
        Assert.False(workbook.TryGetWorksheet("Aging", out _));
    }

    private static async Task<XLWorkbook> DownloadAsync(HttpClient client, string url, string name)
    {
        var response = await client.GetAsync(url, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".xlsx", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.True(response.Headers.Contains("X-Report-Run-Id"));
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
    }
}
