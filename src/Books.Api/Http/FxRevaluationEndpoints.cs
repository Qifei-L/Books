using System.Globalization;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class FxRevaluationEndpoints
{
    /// <param name="Rates">Closing rates to use, for example {"KHR": "4100"}; omitted currencies use the rate table on or before the date.</param>
    public sealed record RevaluationRequest(DateOnly Date, IReadOnlyDictionary<string, string>? Rates = null);

    public static void MapFxRevaluationEndpoints(this RouteGroupBuilder api)
    {
        var revaluations = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/fx-revaluations");

        revaluations.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, FxRevaluationService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        // khrRate: optional closing rate for KHR; other currencies use the rate table.
        revaluations.MapGet("/preview", async (Guid orgId, Guid ledgerId, DateOnly date, string? khrRate, PrincipalAccessor principals, FxRevaluationService service,
            HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.PreviewAsync(await principals.GetAsync(ct), orgId, ledgerId, date,
                khrRate is null ? null : Rates(new Dictionary<string, string> { ["KHR"] = khrRate }), ct)));

        revaluations.MapPost("", async (Guid orgId, Guid ledgerId, RevaluationRequest body, PrincipalAccessor principals, FxRevaluationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, body.Date, Rates(body.Rates), ct)));

        revaluations.MapDelete("/{revaluationId:guid}", async (Guid orgId, Guid ledgerId, Guid revaluationId, PrincipalAccessor principals, FxRevaluationService service,
            HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, revaluationId, ct)));
    }

    internal static Dictionary<string, decimal>? Rates(IReadOnlyDictionary<string, string>? rates) =>
        rates?.ToDictionary(r => r.Key.Trim().ToUpperInvariant(), r =>
            decimal.TryParse(r.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate) && rate > 0
                ? rate
                : throw BooksProblem.Invalid($"The {r.Key} rate must be a positive decimal string."));
}
