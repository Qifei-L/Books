using System.Globalization;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class TransferEndpoints
{
    /// <param name="Amount">Decimal string such as "500.00".</param>
    public sealed record TransferRequest(
        DateOnly Date, Guid FromAccountId, Guid ToAccountId, string Amount, string? FeeAmount, Guid? FeeAccountId, string? Reference, string? Description,
        bool Post = true);

    public static void MapTransferEndpoints(this RouteGroupBuilder api)
    {
        var transfers = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/transfers");

        transfers.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, BankTransferService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        transfers.MapPost("", async (Guid orgId, Guid ledgerId, TransferRequest body, PrincipalAccessor principals, BankTransferService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), body.Post, ct)));

        transfers.MapPut("/{transferId:guid}", async (Guid orgId, Guid ledgerId, Guid transferId, TransferRequest body, PrincipalAccessor principals, BankTransferService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, transferId, Input(body), body.Post, ct)));

        transfers.MapDelete("/{transferId:guid}", async (Guid orgId, Guid ledgerId, Guid transferId, PrincipalAccessor principals, BankTransferService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, transferId, ct)));
    }

    private static BankTransferInput Input(TransferRequest body) =>
        new(body.Date, body.FromAccountId, body.ToAccountId, Amount(body.Amount, "amount"), Amount(body.FeeAmount, "fee_amount"), body.FeeAccountId, body.Reference, body.Description);

    private static decimal Amount(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? 0
        : decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) ? amount
        : throw BooksProblem.Invalid($"{field} must be a non-negative decimal string such as \"100.00\".");
}
