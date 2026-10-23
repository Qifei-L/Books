using Books.Api.Auth;
using Books.Application.Reporting;

namespace Books.Api.Http;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/reports");

        reports.MapGet("/trial-balance", async (Guid orgId, Guid ledgerId, DateOnly from, DateOnly to, PrincipalAccessor principals, ReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.TrialBalanceAsync(await principals.GetAsync(ct), orgId, ledgerId, from, to, ct)));

        reports.MapGet("/gl-detail", async (Guid orgId, Guid ledgerId, Guid accountId, DateOnly from, DateOnly to, PrincipalAccessor principals, ReportService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.GlDetailAsync(await principals.GetAsync(ct), orgId, ledgerId, accountId, from, to, ct)));
    }
}
