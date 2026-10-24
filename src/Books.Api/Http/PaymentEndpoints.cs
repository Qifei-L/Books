using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class PaymentEndpoints
{
    /// <param name="InvoiceId">Settles this invoice.</param>
    /// <param name="AccountId">Posts to this account. An account with the Payable or Receivable usage settles with the party
    /// (Manager.io style), optionally naming an invoice; with no account, the line settles on the document's own side.</param>
    /// <param name="Amount">Decimal string; net of VAT on account lines; may be negative.</param>
    public sealed record LineRequest(
        Guid? InvoiceId, Guid? AccountId, string? Description, Guid? ItemId, string? Quantity, string? UnitPrice, string? Amount,
        Guid? TaxCodeId, string? TaxAmount, string? TaxReason, bool? ValidTaxInvoice, Guid? WithholdingCodeId, string? SettledAmount = null);

    public sealed record PaymentRequest(
        Guid BankAccountId, Guid? PartyId, DateOnly Date, string? Reference, string? Description, IReadOnlyList<LineRequest> Lines,
        bool Post = true, Guid? ControlAccountId = null, string? ExchangeRate = null);

    public static void MapPaymentEndpoints(this RouteGroupBuilder api)
    {
        // Payments and receipts share one model and endpoint shape, with their own lists and number series.
        foreach (var (path, receipts) in new[] { ("payments", false), ("receipts", true) })
        {
            var group = api.MapGroup($"/organizations/{{orgId:guid}}/ledgers/{{ledgerId:guid}}/{path}");

            group.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, PaymentService service, HttpContext http, CancellationToken ct) =>
                Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, receipts, ct)));

            group.MapPost("", async (Guid orgId, Guid ledgerId, PaymentRequest body, PrincipalAccessor principals, PaymentService service, HttpContext http, CancellationToken ct) =>
                WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), body.Post, receipts, ct)));

            group.MapPut("/{paymentId:guid}", async (Guid orgId, Guid ledgerId, Guid paymentId, PaymentRequest body, PrincipalAccessor principals, PaymentService service, HttpContext http, CancellationToken ct) =>
                WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, paymentId, Input(body), body.Post, ct)));

            group.MapDelete("/{paymentId:guid}", async (Guid orgId, Guid ledgerId, Guid paymentId, PrincipalAccessor principals, PaymentService service, HttpContext http, CancellationToken ct) =>
                WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, paymentId, ct)));
        }
    }

    internal static PaymentInput Input(PaymentRequest body) => new(
        body.BankAccountId, body.PartyId, body.Date, body.Reference, body.Description,
        (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => new PaymentLineInput(
            l.InvoiceId, l.AccountId, l.Description, l.ItemId, PurchaseEndpoints.Number(l.Quantity, i, "quantity", signed: true),
            PurchaseEndpoints.Number(l.UnitPrice, i, "unit_price"), PurchaseEndpoints.Number(l.Amount, i, "amount", signed: true),
            l.TaxCodeId, PurchaseEndpoints.Number(l.TaxAmount, i, "tax_amount", signed: true), l.TaxReason, l.ValidTaxInvoice ?? true, l.WithholdingCodeId,
            PurchaseEndpoints.Number(l.SettledAmount, i, "settled_amount"))).ToList(),
        body.ControlAccountId, PurchaseEndpoints.Rate(body.ExchangeRate));
}
