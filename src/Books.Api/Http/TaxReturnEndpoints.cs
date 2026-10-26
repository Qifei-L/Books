using Books.Api.Auth;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class TaxReturnEndpoints
{
    public static void MapTaxReturnEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/tax-pack", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.PackAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        // A month's return, yyyy-MM. khrRate with khrReason recalculates the tax currency at one rate for the month.
        // definitionVersion previews the month with a specific (for example draft) definition version.
        ledger.MapGet("/tax-returns/{period}", async (Guid orgId, Guid ledgerId, string period, string? khrRate, string? khrReason, int? definitionVersion,
            PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ReturnAsync(await principals.GetAsync(ct), orgId, ledgerId, period, Override(khrRate, khrReason), ct, definitionVersion)));

        ledger.MapGet("/tax-returns", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.FiledAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        // Filing keeps the return as filed and closes the ledger through the month end (administrators only).
        ledger.MapPost("/tax-returns/{period}/file", async (Guid orgId, Guid ledgerId, string period, FileRequest? body,
            PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.FileAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, period,
                Override(body?.KhrRate, body?.KhrReason), ct)));

        ledger.MapGet("/tax-returns/{period}.xlsx", async (Guid orgId, Guid ledgerId, string period, string? khrRate, string? khrReason, int? definitionVersion,
            PrincipalAccessor principals, TaxReturnService service, Books.Application.Ledgers.CompanyService companies, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var r = await service.ReturnAsync(principal, orgId, ledgerId, period, Override(khrRate, khrReason), ct, definitionVersion);
            var name = (await companies.GetLedgerAsync(principal, orgId, ledgerId, ct)).CompanyName;
            return Results.File(ExcelExport.TaxReturn(r, name), ExcelExport.ContentType, $"tax-return-{r.Period}.xlsx");
        });

        ledger.MapGet("/tax-returns/{period}.pdf", async (Guid orgId, Guid ledgerId, string period, string? khrRate, string? khrReason, int? definitionVersion,
            PrincipalAccessor principals, TaxReturnService service, Books.Application.Reporting.ReportDeliverySettingsService settings,
            Books.Application.Reporting.ReportTemplateService templates, Books.Application.Reporting.ReportRunService runs,
            Books.Application.Ledgers.CompanyService companies, HttpContext http, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var r = await service.ReturnAsync(principal, orgId, ledgerId, period, Override(khrRate, khrReason), ct, definitionVersion);
            var company = (await companies.GetLedgerAsync(principal, orgId, ledgerId, ct)).CompanyName;
            var branding = await settings.GetBrandingAsync(principal, orgId, ct);
            var template = await templates.ResolveDetailedAsync(orgId, ledgerId, Books.Domain.Reporting.ReportTemplateKind.TaxWorkpaper, r.To, ct);
            var pdf = Books.Api.Reporting.StimulsoftReportRenderer.RenderTaxWorkpaper(r, branding, company, template.Spec.EnabledSections);
            var name = $"tax-workpaper-{r.Period}.pdf";
            var run = await runs.RecordAsync(principal, orgId, ledgerId, new Books.Application.Reporting.ReportRunInput("tax_workpaper",
                template.Id, r.Period, "pdf", name, "application/pdf",
                new Books.Api.Reporting.TaxWorkpaperSnapshot(r, branding, company, template.Spec.EnabledSections), http.TraceIdentifier), pdf, ct);
            http.Response.Headers["X-Report-Run-Id"] = run.Id.ToString();
            return Results.File(pdf, "application/pdf", name);
        });

        // The tax workpaper workbook: boxes, reconciliation and issues as separate sheets.
        ledger.MapGet("/tax-returns/{period}/tax-workpaper.xlsx", async (Guid orgId, Guid ledgerId, string period, string? khrRate, string? khrReason,
            int? definitionVersion, PrincipalAccessor principals, TaxReturnService service, Books.Application.Reporting.ReportTemplateService templates,
            Books.Application.Reporting.ReportRunService runs, Books.Application.Reporting.ReportDeliverySettingsService settings,
            Books.Application.Ledgers.CompanyService companies, HttpContext http, CancellationToken ct) =>
        {
            var principal = await principals.GetAsync(ct);
            var r = await service.ReturnAsync(principal, orgId, ledgerId, period, Override(khrRate, khrReason), ct, definitionVersion);
            var company = (await companies.GetLedgerAsync(principal, orgId, ledgerId, ct)).CompanyName;
            var branding = await settings.GetBrandingAsync(principal, orgId, ct);
            var template = await templates.ResolveDetailedAsync(orgId, ledgerId, Books.Domain.Reporting.ReportTemplateKind.TaxWorkpaper, r.To, ct);
            var xlsx = ExcelExport.TaxWorkpaper(r, company);
            var name = $"tax-workpaper-{r.Period}.xlsx";
            var run = await runs.RecordAsync(principal, orgId, ledgerId, new Books.Application.Reporting.ReportRunInput("tax_workpaper",
                template.Id, r.Period, "xlsx", name, ExcelExport.ContentType,
                new Books.Api.Reporting.TaxWorkpaperSnapshot(r, branding, company, template.Spec.EnabledSections), http.TraceIdentifier), xlsx, ct);
            http.Response.Headers["X-Report-Run-Id"] = run.Id.ToString();
            return Results.File(xlsx, ExcelExport.ContentType, name);
        });

        ledger.MapGet("/tax-returns/{period}/boxes/{key}", async (Guid orgId, Guid ledgerId, string period, string key, string? khrRate, string? khrReason, int? definitionVersion,
            PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.BoxLinesAsync(await principals.GetAsync(ct), orgId, ledgerId, period, key, Override(khrRate, khrReason), ct, definitionVersion)));

        // Versions of the return definition: drafts by anyone who edits the ledger (AI clients too), published by administrators.
        var definitions = ledger.MapGroup("/tax-return-definitions");
        definitions.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, ReturnDefinitionService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));
        definitions.MapGet("/{number:int}", async (Guid orgId, Guid ledgerId, int number, PrincipalAccessor principals, ReturnDefinitionService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.GetAsync(await principals.GetAsync(ct), orgId, ledgerId, number, ct)));
        definitions.MapPost("", async (Guid orgId, Guid ledgerId, DefinitionRequest body, PrincipalAccessor principals, ReturnDefinitionService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateDraftAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, body.EffectiveFrom, body.Definition, ct)));
        definitions.MapPut("/{number:int}", async (Guid orgId, Guid ledgerId, int number, DefinitionRequest body, PrincipalAccessor principals, ReturnDefinitionService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateDraftAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, number, body.EffectiveFrom, body.Definition, ct)));
        definitions.MapPost("/{number:int}/publish", async (Guid orgId, Guid ledgerId, int number, PrincipalAccessor principals, ReturnDefinitionService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.PublishAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, number, ct)));
    }

    public sealed record FileRequest(string? KhrRate, string? KhrReason);

    public sealed record DefinitionRequest(string EffectiveFrom, Books.Domain.Documents.ReturnDefinitionSpec Definition);

    internal static ReturnRateOverride? Override(string? rate, string? reason) =>
        rate is null && reason is null ? null
        : new ReturnRateOverride(PurchaseEndpoints.Rate(rate) ?? throw Books.Application.Common.BooksProblem.Invalid("khrRate is required with khrReason."), reason ?? "");
}
