using Books.Application.DTOs;
using Books.Application.Reports;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api.Controllers;

[ApiController]
public class ReportsController(ReportService reportService) : ControllerBase
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
}
