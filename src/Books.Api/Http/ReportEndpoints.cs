using Books.Api.Auth;
using Books.Application.Ledgers;
using Books.Application.Reporting;

namespace Books.Api.Http;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/reports");

        reports.MapGet("/trial-balance", async (Guid orgId, Guid ledgerId, DateOnly from, DateOnly to, PrincipalAccessor principals, ReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.TrialBalanceAsync(await principals.GetAsync(ct), orgId, ledgerId, from, to, ct)));

        reports.MapGet("/profit-and-loss", async (Guid orgId, Guid ledgerId, DateOnly from, DateOnly to, PrincipalAccessor principals, StatementService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ProfitAndLossAsync(await principals.GetAsync(ct), orgId, ledgerId, from, to, ct)));

        reports.MapGet("/balance-sheet", async (Guid orgId, Guid ledgerId, DateOnly asOf, PrincipalAccessor principals, StatementService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.BalanceSheetAsync(await principals.GetAsync(ct), orgId, ledgerId, asOf, ct)));

        reports.MapGet("/gl-detail", async (Guid orgId, Guid ledgerId, Guid accountId, DateOnly from, DateOnly to, PrincipalAccessor principals, ReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.GlDetailAsync(await principals.GetAsync(ct), orgId, ledgerId, accountId, from, to, ct)));

        reports.MapGet("/ap-aging", async (Guid orgId, Guid ledgerId, DateOnly asOf, PrincipalAccessor principals, PayablesReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.AgingAsync(await principals.GetAsync(ct), orgId, ledgerId, asOf, ct)));

        reports.MapGet("/supplier-statement", async (Guid orgId, Guid ledgerId, Guid supplierId, DateOnly from, DateOnly to, PrincipalAccessor principals, PayablesReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.StatementAsync(await principals.GetAsync(ct), orgId, ledgerId, supplierId, from, to, ct)));

        reports.MapGet("/ar-aging", async (Guid orgId, Guid ledgerId, DateOnly asOf, PrincipalAccessor principals, ReceivablesReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.AgingAsync(await principals.GetAsync(ct), orgId, ledgerId, asOf, ct)));

        reports.MapGet("/customer-statement", async (Guid orgId, Guid ledgerId, Guid customerId, DateOnly from, DateOnly to, PrincipalAccessor principals, ReceivablesReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.StatementAsync(await principals.GetAsync(ct), orgId, ledgerId, customerId, from, to, ct)));

        reports.MapGet("/inventory", async (Guid orgId, Guid ledgerId, DateOnly asOf, PrincipalAccessor principals, Books.Application.Documents.InventoryReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ReportAsync(await principals.GetAsync(ct), orgId, ledgerId, asOf, ct)));

        // Excel versions of the same reports (owner decision: Excel is the only export format for now).
        reports.MapGet("/trial-balance.xlsx", async (Guid orgId, Guid ledgerId, DateOnly from, DateOnly to, PrincipalAccessor principals, ReportService service, CompanyService ledgers, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var tb = await service.TrialBalanceAsync(principal, orgId, ledgerId, from, to, ct);
            return Xlsx(ExcelExport.TrialBalance(tb, await NameAsync(ledgers, principal, orgId, ledgerId, ct)), $"trial-balance-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}");
        });

        reports.MapGet("/profit-and-loss.xlsx", async (Guid orgId, Guid ledgerId, DateOnly from, DateOnly to, PrincipalAccessor principals, StatementService service, CompanyService ledgers, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var pl = await service.ProfitAndLossAsync(principal, orgId, ledgerId, from, to, ct);
            return Xlsx(ExcelExport.ProfitAndLoss(pl, await NameAsync(ledgers, principal, orgId, ledgerId, ct)), $"profit-and-loss-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}");
        });

        reports.MapGet("/balance-sheet.xlsx", async (Guid orgId, Guid ledgerId, DateOnly asOf, PrincipalAccessor principals, StatementService service, CompanyService ledgers, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var bs = await service.BalanceSheetAsync(principal, orgId, ledgerId, asOf, ct);
            return Xlsx(ExcelExport.BalanceSheet(bs, await NameAsync(ledgers, principal, orgId, ledgerId, ct)), $"balance-sheet-{asOf:yyyy-MM-dd}");
        });

        reports.MapGet("/gl-detail.xlsx", async (Guid orgId, Guid ledgerId, Guid accountId, DateOnly from, DateOnly to, PrincipalAccessor principals, ReportService service, CompanyService ledgers, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var gl = await service.GlDetailAsync(principal, orgId, ledgerId, accountId, from, to, ct);
            return Xlsx(ExcelExport.GlDetail(gl, await NameAsync(ledgers, principal, orgId, ledgerId, ct)), $"gl-detail-{gl.Code}-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}");
        });
    }

    private static async Task<string> NameAsync(CompanyService ledgers, Books.Application.Access.Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        (await ledgers.GetLedgerAsync(principal, orgId, ledgerId, ct)).CompanyName;

    private static IResult Xlsx(byte[] content, string name) => Results.File(content, ExcelExport.ContentType, $"{name}.xlsx");
}
