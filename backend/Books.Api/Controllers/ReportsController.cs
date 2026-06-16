using Books.Application.DTOs;
using Books.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api.Controllers;

[ApiController]
public class ReportsController(ReportService reportService, ReportExportService reportExportService) : ControllerBase
{
    [HttpGet("api/v1/ledgers/{ledgerId:int}/reports")]
    public async Task<ActionResult<List<ReportSummaryDto>>> Reports(int ledgerId)
    {
        return await reportService.GetAvailableReportsAsync(ledgerId);
    }

    [HttpGet("api/v1/ledgers/{ledgerId:int}/reports/trial-balance")]
    public async Task<ActionResult<List<TrialBalanceRowDto>>> TrialBalance(int ledgerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        return await reportService.GetTrialBalanceAsync(ledgerId, from, to);
    }

    [HttpGet("api/v1/ledgers/{ledgerId:int}/reports/general-ledger")]
    public async Task<ActionResult<List<GeneralLedgerRowDto>>> GeneralLedger(int ledgerId, [FromQuery] int accountId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        return await reportService.GetGeneralLedgerAsync(ledgerId, accountId, from, to);
    }

    [HttpGet("api/v1/ledgers/{ledgerId:int}/reports/profit-loss")]
    public async Task<ActionResult<List<FinancialStatementRowDto>>> ProfitLoss(int ledgerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        return await reportService.GetProfitLossAsync(ledgerId, from, to);
    }

    [HttpGet("api/v1/ledgers/{ledgerId:int}/reports/balance-sheet")]
    public async Task<ActionResult<List<FinancialStatementRowDto>>> BalanceSheet(int ledgerId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        return await reportService.GetBalanceSheetAsync(ledgerId, from, to);
    }

    [HttpGet("api/v1/reports/trial-balance/export")]
    public async Task<IActionResult> ExportTrialBalance(
        [FromQuery] string? format,
        [FromQuery] int ledgerId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? periodId)
    {
        return await ReportFile(format,
            () => reportExportService.ExportTrialBalanceExcelAsync(ledgerId, dateFrom, dateTo),
            () => reportExportService.ExportTrialBalancePdfAsync(ledgerId, dateFrom, dateTo));
    }

    [HttpGet("api/v1/reports/general-ledger/export")]
    public async Task<IActionResult> ExportGeneralLedger(
        [FromQuery] string? format,
        [FromQuery] int ledgerId,
        [FromQuery] int accountId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? periodId)
    {
        return await ReportFile(format,
            () => reportExportService.ExportGeneralLedgerExcelAsync(ledgerId, accountId, dateFrom, dateTo),
            () => reportExportService.ExportGeneralLedgerPdfAsync(ledgerId, accountId, dateFrom, dateTo));
    }

    [HttpGet("api/v1/reports/profit-and-loss/export")]
    public async Task<IActionResult> ExportProfitLoss(
        [FromQuery] string? format,
        [FromQuery] int ledgerId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? periodId)
    {
        return await ReportFile(format,
            () => reportExportService.ExportProfitLossExcelAsync(ledgerId, dateFrom, dateTo),
            () => reportExportService.ExportProfitLossPdfAsync(ledgerId, dateFrom, dateTo));
    }

    [HttpGet("api/v1/reports/balance-sheet/export")]
    public async Task<IActionResult> ExportBalanceSheet(
        [FromQuery] string? format,
        [FromQuery] int ledgerId,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int? periodId)
    {
        return await ReportFile(format,
            () => reportExportService.ExportBalanceSheetExcelAsync(ledgerId, dateFrom, dateTo),
            () => reportExportService.ExportBalanceSheetPdfAsync(ledgerId, dateFrom, dateTo));
    }

    private async Task<IActionResult> ReportFile(
        string? format,
        Func<Task<ReportExportResult>> excel,
        Func<Task<ReportExportResult>> pdf)
    {
        var report = format?.Trim().ToLowerInvariant() switch
        {
            "excel" => await excel(),
            "pdf" => await pdf(),
            _ => null
        };

        return report is null
            ? BadRequest(new { error = "Invalid format. Allowed values are excel and pdf." })
            : File(report.Content, report.ContentType, report.FileName);
    }
}
