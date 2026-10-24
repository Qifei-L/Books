using System.Globalization;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Ledgers;
using Books.Domain.Ledgers;

namespace Books.Api.Http;

public static class TaxCodeEndpoints
{
    /// <param name="Rate">Percent as a decimal string, for example "10" or "4.5".</param>
    public sealed record TaxCodeRequest(
        string Name, string Kind, string Rate, Guid? AccountId, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool? Active, Guid? NonDeductibleAccountId = null, string? ReturnCategory = null);

    public static void MapTaxCodeEndpoints(this RouteGroupBuilder api)
    {
        var codes = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/tax-codes");

        codes.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, TaxCodeService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        codes.MapPost("", async (Guid orgId, Guid ledgerId, TaxCodeRequest body, PrincipalAccessor principals, TaxCodeService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), ct)));

        codes.MapPut("/{taxCodeId:guid}", async (Guid orgId, Guid ledgerId, Guid taxCodeId, TaxCodeRequest body, PrincipalAccessor principals, TaxCodeService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, taxCodeId, Input(body), ct)));
    }

    private static TaxCodeInput Input(TaxCodeRequest body) => new(
        body.Name,
        body.Kind switch
        {
            "vat_output" => TaxKind.VatOutput,
            "vat_input" => TaxKind.VatInput,
            "vat_zero_rated" => TaxKind.VatZeroRated,
            "non_vat" => TaxKind.NonVat,
            "withholding" => TaxKind.Withholding,
            "other_tax" => TaxKind.OtherTax,
            _ => throw BooksProblem.Invalid("kind must be vat_output, vat_input, vat_zero_rated, non_vat, withholding or other_tax."),
        },
        decimal.TryParse(body.Rate, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate)
            ? rate
            : throw BooksProblem.Invalid("rate must be a decimal string such as \"10\"."),
        body.AccountId, body.EffectiveFrom, body.EffectiveTo, body.Active ?? true, body.NonDeductibleAccountId, body.ReturnCategory);
}
