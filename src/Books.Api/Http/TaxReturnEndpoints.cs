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
        ledger.MapGet("/tax-returns/{period}", async (Guid orgId, Guid ledgerId, string period, string? khrRate, string? khrReason,
            PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ReturnAsync(await principals.GetAsync(ct), orgId, ledgerId, period, Override(khrRate, khrReason), ct)));

        ledger.MapGet("/tax-returns", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.FiledAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        // Filing keeps the return as filed and closes the ledger through the month end (administrators only).
        ledger.MapPost("/tax-returns/{period}/file", async (Guid orgId, Guid ledgerId, string period, FileRequest? body,
            PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.FileAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, period,
                Override(body?.KhrRate, body?.KhrReason), ct)));

        ledger.MapGet("/tax-returns/{period}/boxes/{key}", async (Guid orgId, Guid ledgerId, string period, string key, string? khrRate, string? khrReason,
            PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.BoxLinesAsync(await principals.GetAsync(ct), orgId, ledgerId, period, key, Override(khrRate, khrReason), ct)));
    }

    public sealed record FileRequest(string? KhrRate, string? KhrReason);

    internal static ReturnRateOverride? Override(string? rate, string? reason) =>
        rate is null && reason is null ? null
        : new ReturnRateOverride(PurchaseEndpoints.Rate(rate) ?? throw Books.Application.Common.BooksProblem.Invalid("khrRate is required with khrReason."), reason ?? "");
}
