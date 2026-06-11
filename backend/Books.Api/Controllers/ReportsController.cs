using Books.Application.DTOs;
using Books.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Books.Api.Controllers;

[ApiController]
public class ReportsController(ReportService reportService) : ControllerBase
{
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
}
