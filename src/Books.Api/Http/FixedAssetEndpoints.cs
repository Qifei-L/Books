using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class FixedAssetEndpoints
{
    /// <param name="Cost">Decimal string.</param>
    public sealed record AssetRequest(
        string Code, string Name, DateOnly AcquisitionDate, string Cost, string? ResidualValue, int UsefulLifeMonths,
        Guid? AssetAccountId = null, Guid? AccumulatedDepreciationAccountId = null, Guid? DepreciationExpenseAccountId = null);

    public sealed record DisposeRequest(DateOnly Date, Guid GainOrLossAccountId);

    /// <param name="Period">yyyy-MM.</param>
    public sealed record RunRequest(string Period);

    public static void MapFixedAssetEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/fixed-assets", async (Guid orgId, Guid ledgerId, DateOnly? asOf, PrincipalAccessor principals, FixedAssetService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.RegisterAsync(await principals.GetAsync(ct), orgId, ledgerId, asOf ?? DateOnly.FromDateTime(DateTime.UtcNow), ct)));

        ledger.MapPost("/fixed-assets", async (Guid orgId, Guid ledgerId, AssetRequest body, PrincipalAccessor principals, FixedAssetService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, null, Input(body), ct)));

        ledger.MapPut("/fixed-assets/{assetId:guid}", async (Guid orgId, Guid ledgerId, Guid assetId, AssetRequest body, PrincipalAccessor principals, FixedAssetService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, assetId, Input(body), ct)));

        ledger.MapPost("/fixed-assets/{assetId:guid}/dispose", async (Guid orgId, Guid ledgerId, Guid assetId, DisposeRequest body, PrincipalAccessor principals, FixedAssetService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DisposeAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, assetId, body.Date, body.GainOrLossAccountId, ct)));

        ledger.MapGet("/depreciation-runs", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, FixedAssetService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.RunsAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        ledger.MapPost("/depreciation-runs", async (Guid orgId, Guid ledgerId, RunRequest body, PrincipalAccessor principals, FixedAssetService service, HttpContext http, CancellationToken ct) =>
        {
            var period = DateOnly.TryParseExact(body.Period + "-01", "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var p)
                ? p : throw BooksProblem.Invalid("period must be a month, yyyy-MM.");
            return WriteHttp.Result(http, await service.RunAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, period, ct));
        });

        ledger.MapDelete("/depreciation-runs/{runId:guid}", async (Guid orgId, Guid ledgerId, Guid runId, PrincipalAccessor principals, FixedAssetService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteRunAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, runId, ct)));
    }

    internal static FixedAssetInput Input(AssetRequest b) => new(
        b.Code, b.Name, b.AcquisitionDate,
        PurchaseEndpoints.Number(b.Cost, 0, "cost") ?? throw BooksProblem.Invalid("cost is required."),
        PurchaseEndpoints.Number(b.ResidualValue, 0, "residual_value") ?? 0,
        b.UsefulLifeMonths, b.AssetAccountId, b.AccumulatedDepreciationAccountId, b.DepreciationExpenseAccountId);
}
