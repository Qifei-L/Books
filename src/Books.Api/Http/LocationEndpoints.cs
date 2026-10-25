using System.Globalization;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class LocationEndpoints
{
    public sealed record LocationRequest(string Name, string? Code, bool? IsDefault, bool? Active);

    /// <param name="Quantity">In the item's base unit, as a decimal string.</param>
    public sealed record TransferLineRequest(Guid ItemId, string Quantity, string? Description);

    public sealed record TransferRequest(DateOnly Date, Guid FromLocationId, Guid ToLocationId, string? Reference, string? Description, IReadOnlyList<TransferLineRequest> Lines);

    public static void MapLocationEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/locations", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, LocationService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));
        ledger.MapPost("/locations", async (Guid orgId, Guid ledgerId, LocationRequest body, PrincipalAccessor principals, LocationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, null, Input(body), ct)));
        ledger.MapPut("/locations/{locationId:guid}", async (Guid orgId, Guid ledgerId, Guid locationId, LocationRequest body, PrincipalAccessor principals, LocationService service,
            HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, locationId, Input(body), ct)));

        ledger.MapGet("/stock-transfers", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, LocationService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.TransfersAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));
        ledger.MapPost("/stock-transfers", async (Guid orgId, Guid ledgerId, TransferRequest body, PrincipalAccessor principals, LocationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveTransferAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, null, Input(body), ct)));
        ledger.MapPut("/stock-transfers/{transferId:guid}", async (Guid orgId, Guid ledgerId, Guid transferId, TransferRequest body, PrincipalAccessor principals, LocationService service,
            HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveTransferAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, transferId, Input(body), ct)));
        ledger.MapDelete("/stock-transfers/{transferId:guid}", async (Guid orgId, Guid ledgerId, Guid transferId, PrincipalAccessor principals, LocationService service,
            HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteTransferAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, transferId, ct)));

        ledger.MapGet("/reports/inventory/locations", async (Guid orgId, Guid ledgerId, DateOnly asOf, PrincipalAccessor principals, LocationService service, HttpContext http,
            CancellationToken ct) =>
            Envelope.Ok(http, await service.StockAsync(await principals.GetAsync(ct), orgId, ledgerId, asOf, ct)));
    }

    private static LocationInput Input(LocationRequest body) => new(body.Name, body.Code, body.IsDefault ?? false, body.Active ?? true);

    private static StockTransferInput Input(TransferRequest body) => new(body.Date, body.FromLocationId, body.ToLocationId, body.Reference, body.Description,
        (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => new StockTransferLineInput(l.ItemId,
            PurchaseEndpoints.Number(l.Quantity, i, "quantity") ?? 0, l.Description)).ToList());
}
