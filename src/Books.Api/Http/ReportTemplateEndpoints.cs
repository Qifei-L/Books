using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Reporting;
using Books.Domain.Reporting;

namespace Books.Api.Http;

public static class ReportTemplateEndpoints
{
    public sealed record TemplateRequest(string Kind, string Name, ReportTemplateSpec Definition, DateOnly? EffectiveFrom);

    public static void MapReportTemplateEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/report-templates");

        ledger.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, ReportTemplateService service, HttpContext http,
            CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        ledger.MapGet("/{id:guid}", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals, ReportTemplateService service,
            HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.GetAsync(await principals.GetAsync(ct), orgId, ledgerId, id, ct)));

        ledger.MapPost("", async (Guid orgId, Guid ledgerId, TemplateRequest body, PrincipalAccessor principals,
            ReportTemplateService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateDraftAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId,
                new ReportTemplateInput(body.Kind, body.Name, body.Definition, body.EffectiveFrom), ct)));

        ledger.MapPut("/{id:guid}", async (Guid orgId, Guid ledgerId, Guid id, TemplateRequest body, PrincipalAccessor principals,
            ReportTemplateService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateDraftAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, id,
                new ReportTemplateInput(body.Kind, body.Name, body.Definition, body.EffectiveFrom), ct)));

        ledger.MapPost("/{id:guid}/publish", async (Guid orgId, Guid ledgerId, Guid id, PrincipalAccessor principals,
            ReportTemplateService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.PublishAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, id, ct)));
    }
}
