using System.Globalization;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class PurchaseEndpoints
{
    /// <param name="Amount">Net amount as a decimal string; omit to use quantity × unit_price.</param>
    /// <param name="TaxAmount">Optional override of the calculated VAT.</param>
    public sealed record LineRequest(
        Guid AccountId, string? Description, Guid? ItemId, string? Quantity, string? UnitPrice, string? Amount,
        Guid? TaxCodeId, string? TaxAmount, string? TaxReason, bool? ValidTaxInvoice, Guid? WithholdingCodeId, string? Unit = null);

    public sealed record InvoiceRequest(
        Guid SupplierId, Guid? PayableAccountId, DateOnly Date, DateOnly? DueDate, string? Reference, string? Description,
        IReadOnlyList<LineRequest> Lines, bool Post = true, Guid? OriginalInvoiceId = null, string? TaxCurrencyRate = null, string? TaxCurrencyReason = null,
        string? Currency = null, string? ExchangeRate = null);

    public static void MapPurchaseEndpoints(this RouteGroupBuilder api)
    {
        var invoices = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/purchase-invoices");

        invoices.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        invoices.MapPost("", async (Guid orgId, Guid ledgerId, InvoiceRequest body, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), body.Post, ct)));

        invoices.MapPut("/{invoiceId:guid}", async (Guid orgId, Guid ledgerId, Guid invoiceId, InvoiceRequest body, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, invoiceId, Input(body), body.Post, ct)));

        // Debit notes share the invoice model and endpoints' shape, with their own list and number series.
        var notes = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/debit-notes");

        notes.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct, debitNotes: true)));

        notes.MapPost("", async (Guid orgId, Guid ledgerId, InvoiceRequest body, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), body.Post, ct, debitNote: true)));

        notes.MapPut("/{invoiceId:guid}", async (Guid orgId, Guid ledgerId, Guid invoiceId, InvoiceRequest body, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, invoiceId, Input(body), body.Post, ct)));

        notes.MapDelete("/{invoiceId:guid}", async (Guid orgId, Guid ledgerId, Guid invoiceId, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, invoiceId, ct)));

        invoices.MapDelete("/{invoiceId:guid}", async (Guid orgId, Guid ledgerId, Guid invoiceId, PrincipalAccessor principals, PurchaseInvoiceService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, invoiceId, ct)));
    }

    internal static PurchaseInvoiceInput Input(InvoiceRequest body) => new(
        body.SupplierId, body.PayableAccountId, body.Date, body.DueDate, body.Reference, body.Description,
        (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => new PurchaseLineInput(
            l.AccountId, l.Description, l.ItemId, Number(l.Quantity, i, "quantity", signed: true), Number(l.UnitPrice, i, "unit_price"),
            Number(l.Amount, i, "amount", signed: true), l.TaxCodeId, Number(l.TaxAmount, i, "tax_amount", signed: true), l.TaxReason,
            l.ValidTaxInvoice ?? true, l.WithholdingCodeId, l.Unit)).ToList(),
        body.OriginalInvoiceId,
        body.TaxCurrencyRate is null && body.TaxCurrencyReason is null ? null
            : new TaxCurrencyInput(Rate(body.TaxCurrencyRate), body.TaxCurrencyReason),
        body.Currency, Rate(body.ExchangeRate));

    internal static decimal? Rate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n
        : throw BooksProblem.Invalid("tax_currency_rate must be a decimal string.");

    internal static decimal? Number(string? value, int index, string field, bool signed = false) =>
        string.IsNullOrWhiteSpace(value) ? null
        : decimal.TryParse(value, NumberStyles.AllowDecimalPoint | (signed ? NumberStyles.AllowLeadingSign : NumberStyles.None), CultureInfo.InvariantCulture, out var n) ? n
        : throw BooksProblem.Invalid($"Line {index + 1} {field} must be a decimal string.");
}
