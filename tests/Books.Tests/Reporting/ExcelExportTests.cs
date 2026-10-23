using System.Net;
using Books.Tests.Infrastructure;
using ClosedXML.Excel;

namespace Books.Tests.Reporting;

/// <summary>Excel exports contain the same figures as the JSON reports, as real numbers, behind the same access check.</summary>
public sealed class ExcelExportTests(TestHost host)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Balance_sheet_export_matches_the_api_and_keeps_numbers_numeric()
    {
        var books = await new StatementTests(host).SeedAsync();

        using var book = await DownloadAsync(books, "balance-sheet.xlsx?asOf=2026-12-31");

        var sheet = book.Worksheet("Balance Sheet");
        var cells = sheet.CellsUsed().ToList();
        var totalAssets = cells.Single(c => c.GetString() == "Total Assets").CellRight();
        Assert.Equal(XLDataType.Number, totalAssets.DataType);
        Assert.Equal(1650m, totalAssets.GetValue<decimal>());
        Assert.Equal(330m, cells.Single(c => c.GetString().EndsWith("Current year profit (calculated)")).CellRight().GetValue<decimal>());
        Assert.Contains(cells, c => c.GetString().StartsWith("Balanced"));
        Assert.Equal(1500m, cells.Single(c => c.GetString() == "Total Bank accounts").CellRight().GetValue<decimal>());
    }

    [Fact]
    public async Task Profit_and_loss_trial_balance_and_gl_detail_export()
    {
        var books = await new StatementTests(host).SeedAsync();

        using var pl = await DownloadAsync(books, "profit-and-loss.xlsx?from=2026-01-01&to=2026-12-31");
        using var tb = await DownloadAsync(books, "trial-balance.xlsx?from=2026-01-01&to=2026-12-31");
        var cash = (await StatementTests.GetAsync(books, "balance-sheet?asOf=2026-12-31")).GetProperty("sections")[0].GetProperty("lines")[0]
            .GetProperty("children").EnumerateArray().Single(l => l.GetProperty("name").GetString() == "Cash").GetProperty("id").GetGuid();
        using var gl = await DownloadAsync(books, $"gl-detail.xlsx?accountId={cash}&from=2026-01-01&to=2026-12-31");

        Assert.Equal(330m, pl.Worksheet(1).CellsUsed().Single(c => c.GetString() == "Net profit").CellRight().GetValue<decimal>());
        Assert.Contains(tb.Worksheet(1).CellsUsed(), c => c.GetString() == "Balanced.");
        Assert.Equal(150m, gl.Worksheet(1).CellsUsed().Single(c => c.GetString() == "Period totals / closing balance").CellRight(3).GetValue<decimal>());
    }

    [Fact]
    public async Task Exports_need_the_same_access_as_the_reports()
    {
        var books = await new StatementTests(host).SeedAsync();
        using var outsider = host.Client(Ids.Subject("outsider"));
        await outsider.CreateOrganizationAsync();

        var response = await outsider.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/balance-sheet.xlsx?asOf=2026-12-31", Ct);

        await response.AssertProblemAsync(HttpStatusCode.NotFound, "NOT_FOUND");
    }

    private static async Task<XLWorkbook> DownloadAsync(StatementTests.Books books, string report)
    {
        var response = await books.Admin.GetAsync($"/api/v1/organizations/{books.OrgId}/ledgers/{books.LedgerId}/reports/{report}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".xlsx", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
    }
}
