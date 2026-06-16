using Books.Application.DTOs;
using Books.Application.Reports;

namespace Books.Blazor;

public static class ReportExportEndpoints
{
    public static IEndpointRouteBuilder MapReportExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/reports/trial-balance/export", async (
            string? format,
            int ledgerId,
            DateTime? dateFrom,
            DateTime? dateTo,
            int? periodId,
            ReportExportService exports) => await ReportFile(format,
                () => exports.ExportTrialBalanceExcelAsync(ledgerId, dateFrom, dateTo),
                () => exports.ExportTrialBalancePdfAsync(ledgerId, dateFrom, dateTo)));

        endpoints.MapGet("/api/v1/reports/general-ledger/export", async (
            string? format,
            int ledgerId,
            int accountId,
            DateTime? dateFrom,
            DateTime? dateTo,
            int? periodId,
            ReportExportService exports) => await ReportFile(format,
                () => exports.ExportGeneralLedgerExcelAsync(ledgerId, accountId, dateFrom, dateTo),
                () => exports.ExportGeneralLedgerPdfAsync(ledgerId, accountId, dateFrom, dateTo)));

        endpoints.MapGet("/api/v1/reports/profit-and-loss/export", async (
            string? format,
            int ledgerId,
            DateTime? dateFrom,
            DateTime? dateTo,
            int? periodId,
            ReportExportService exports) => await ReportFile(format,
                () => exports.ExportProfitLossExcelAsync(ledgerId, dateFrom, dateTo),
                () => exports.ExportProfitLossPdfAsync(ledgerId, dateFrom, dateTo)));

        endpoints.MapGet("/api/v1/reports/balance-sheet/export", async (
            string? format,
            int ledgerId,
            DateTime? dateFrom,
            DateTime? dateTo,
            int? periodId,
            ReportExportService exports) => await ReportFile(format,
                () => exports.ExportBalanceSheetExcelAsync(ledgerId, dateFrom, dateTo),
                () => exports.ExportBalanceSheetPdfAsync(ledgerId, dateFrom, dateTo)));

        return endpoints;
    }

    private static async Task<IResult> ReportFile(
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
            ? Results.BadRequest(new { error = "Invalid format. Allowed values are excel and pdf." })
            : Results.File(report.Content, report.ContentType, report.FileName);
    }
}
