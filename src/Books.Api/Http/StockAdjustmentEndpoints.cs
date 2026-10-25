using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class StockAdjustmentEndpoints
{
    /// <param name="Quantity">Signed change as a decimal string; give this or counted_quantity.</param>
    /// <param name="CountedQuantity">The quantity counted on the date.</param>
    /// <param name="UnitCost">Cost of stock coming in; omit for the average cost.</param>
    /// <param name="AccountId">Omit for the cost of sales account.</param>
    /// <param name="Amount">Signed change of value as a decimal string; alone it revalues the stock.</param>
    public sealed record LineRequest(Guid ItemId, string? Description, string? Quantity, string? CountedQuantity, string? UnitCost, Guid? AccountId,
        string? Amount = null, Guid? LocationId = null);

    /// <param name="Debit">Decimal string; an entry has a debit or a credit, like a journal line.</param>
    public sealed record EntryRequest(Guid AccountId, string? Debit, string? Credit, string? Description);

    /// <param name="Entries">The other side on one or more accounts, balancing the item lines; omit to use each line's account (default the item's cost of sales).</param>
    public sealed record AdjustmentRequest(DateOnly Date, string? Reference, string? Description, IReadOnlyList<LineRequest> Lines, bool Post = true,
        IReadOnlyList<EntryRequest>? Entries = null);

    public static void MapStockAdjustmentEndpoints(this RouteGroupBuilder api)
    {
        var adjustments = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/stock-adjustments");

        adjustments.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, StockAdjustmentService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        adjustments.MapPost("", async (Guid orgId, Guid ledgerId, AdjustmentRequest body, PrincipalAccessor principals, StockAdjustmentService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), body.Post, ct)));

        // What an adjustment would post, without saving it; adjustmentId leaves an edited adjustment out of the stock it is measured against.
        adjustments.MapPost("/preview", async (Guid orgId, Guid ledgerId, Guid? adjustmentId, AdjustmentRequest body, PrincipalAccessor principals, StockAdjustmentService service,
            HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.PreviewAsync(await principals.GetAsync(ct), orgId, ledgerId, adjustmentId, Input(body), ct)));

        adjustments.MapPut("/{adjustmentId:guid}", async (Guid orgId, Guid ledgerId, Guid adjustmentId, AdjustmentRequest body, PrincipalAccessor principals, StockAdjustmentService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, adjustmentId, Input(body), body.Post, ct)));

        adjustments.MapDelete("/{adjustmentId:guid}", async (Guid orgId, Guid ledgerId, Guid adjustmentId, PrincipalAccessor principals, StockAdjustmentService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, adjustmentId, ct)));
    }

    internal static StockAdjustmentInput Input(AdjustmentRequest body) => new(body.Date, body.Reference, body.Description,
        (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => new StockAdjustmentLineInput(l.ItemId, l.Description,
            PurchaseEndpoints.Number(l.Quantity, i, "quantity", signed: true), PurchaseEndpoints.Number(l.CountedQuantity, i, "counted_quantity"),
            PurchaseEndpoints.Number(l.UnitCost, i, "unit_cost"), l.AccountId, PurchaseEndpoints.Number(l.Amount, i, "amount", signed: true), l.LocationId)).ToList(),
        body.Entries?.Select((e, i) => new StockAdjustmentEntryInput(e.AccountId,
            (PurchaseEndpoints.Number(e.Debit, i, "debit") ?? 0) - (PurchaseEndpoints.Number(e.Credit, i, "credit") ?? 0), e.Description)).ToList());
}
