using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class SalesEndpoints
{
    /// <param name="Amount">Net amount as a decimal string; omit to use quantity × unit_price.</param>
    /// <param name="TaxAmount">Optional override of the calculated VAT.</param>
    public sealed record LineRequest(
        Guid AccountId, string? Description, Guid? ItemId, string? Quantity, string? UnitPrice, string? Amount,
        Guid? TaxCodeId, string? TaxAmount, string? TaxReason, Guid? OtherTaxCodeId = null, string? Unit = null, Guid? LocationId = null);

    public sealed record InvoiceRequest(
        Guid CustomerId, Guid? ReceivableAccountId, DateOnly Date, DateOnly? DueDate, string? Reference, string? Description,
        IReadOnlyList<LineRequest> Lines, bool Post = true, Guid? OriginalInvoiceId = null, string? TaxCurrencyRate = null, string? TaxCurrencyReason = null,
        string? Currency = null, string? ExchangeRate = null);

    public sealed record InvoiceSettingsRequest(
        string? NameKhmer, string? NameEnglish, string? TaxId, string? AddressKhmer, string? AddressEnglish, string? Phone, string? Email,
        string? InvoicePrefix, string? CreditNotePrefix);

    public static void MapSalesEndpoints(this RouteGroupBuilder api)
    {
        // Invoices and credit notes share one model and endpoint shape, with their own lists and number series.
        foreach (var (path, creditNotes) in new[] { ("sales-invoices", false), ("credit-notes", true) })
        {
            var group = api.MapGroup($"/organizations/{{orgId:guid}}/ledgers/{{ledgerId:guid}}/{path}");

            group.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, SalesInvoiceService service, HttpContext http, CancellationToken ct) =>
                Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct, creditNotes)));

            group.MapPost("", async (Guid orgId, Guid ledgerId, InvoiceRequest body, PrincipalAccessor principals, SalesInvoiceService service, HttpContext http, CancellationToken ct) =>
                WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), body.Post, ct, creditNotes)));

            group.MapPut("/{invoiceId:guid}", async (Guid orgId, Guid ledgerId, Guid invoiceId, InvoiceRequest body, PrincipalAccessor principals, SalesInvoiceService service, HttpContext http, CancellationToken ct) =>
                WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, invoiceId, Input(body), body.Post, ct)));

            // style: tax, zero_non_vat or commercial (omit to choose from the lines' tax codes); stateCharge labels VAT as state charge.
            group.MapGet("/{invoiceId:guid}/pdf", async (Guid orgId, Guid ledgerId, Guid invoiceId, string? style, bool? stateCharge, PrincipalAccessor principals,
                InvoicePrintService service, HttpContext http, CancellationToken ct) =>
            {
                var print = await service.GetAsync(await principals.GetAsync(ct), orgId, ledgerId, invoiceId, creditNotes, style, stateCharge ?? false, ct);
                http.Response.Headers["Invoice-Style"] = print.Style;
                return Results.File(InvoicePdf.Render(print), InvoicePdf.ContentType, $"{print.Number}.pdf");
            });

            group.MapDelete("/{invoiceId:guid}", async (Guid orgId, Guid ledgerId, Guid invoiceId, PrincipalAccessor principals, SalesInvoiceService service, HttpContext http, CancellationToken ct) =>
                WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, invoiceId, ct)));
        }

        var settings = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/invoice-settings");

        settings.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, InvoiceSettingsService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.GetAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        settings.MapPut("", async (Guid orgId, Guid ledgerId, InvoiceSettingsRequest body, PrincipalAccessor principals, InvoiceSettingsService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SetAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId,
                new InvoiceSettingsInput(body.NameKhmer, body.NameEnglish, body.TaxId, body.AddressKhmer, body.AddressEnglish, body.Phone, body.Email,
                    body.InvoicePrefix, body.CreditNotePrefix), ct)));
    }

    internal static SalesInvoiceInput Input(InvoiceRequest body) => new(
        body.CustomerId, body.ReceivableAccountId, body.Date, body.DueDate, body.Reference, body.Description,
        (body.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => new SalesLineInput(
            l.AccountId, l.Description, l.ItemId, PurchaseEndpoints.Number(l.Quantity, i, "quantity", signed: true), PurchaseEndpoints.Number(l.UnitPrice, i, "unit_price"),
            PurchaseEndpoints.Number(l.Amount, i, "amount", signed: true), l.TaxCodeId, PurchaseEndpoints.Number(l.TaxAmount, i, "tax_amount", signed: true), l.TaxReason, l.OtherTaxCodeId, l.Unit, l.LocationId)).ToList(),
        body.OriginalInvoiceId,
        body.TaxCurrencyRate is null && body.TaxCurrencyReason is null ? null
            : new TaxCurrencyInput(PurchaseEndpoints.Rate(body.TaxCurrencyRate), body.TaxCurrencyReason),
        body.Currency, PurchaseEndpoints.Rate(body.ExchangeRate));
}
