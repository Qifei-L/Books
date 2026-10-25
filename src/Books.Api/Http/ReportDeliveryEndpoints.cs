using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Reporting;

namespace Books.Api.Http;

public static class ReportDeliveryEndpoints
{
    public sealed record BrandingRequest(string? LegalName, string? LegalNameKm, string? VatTin, string? Address, string? Phone,
        string? Email, string? BrandColor, string? Footer, string? PreparedBy, string? ReviewedBy);

    public sealed record SettingsRequest(string? Language, string? Comparative, bool? ShowZeroBalances, bool? ShowAccountCodes,
        string? PaperSize, string? Orientation, string? Watermark, string? PackSections, int? ShareDays);

    public static void MapReportDeliveryEndpoints(this RouteGroupBuilder api)
    {
        var org = api.MapGroup("/organizations/{orgId:guid}");
        org.MapGet("/branding", async (Guid orgId, PrincipalAccessor principals, ReportDeliverySettingsService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.GetBrandingAsync(await principals.GetAsync(ct), orgId, ct)));
        org.MapPut("/branding", async (Guid orgId, BrandingRequest body, PrincipalAccessor principals, ReportDeliverySettingsService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveBrandingAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId,
                new BrandingInput(body.LegalName, body.LegalNameKm, body.VatTin, body.Address, body.Phone, body.Email, body.BrandColor, body.Footer, body.PreparedBy, body.ReviewedBy), ct)));

        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");
        ledger.MapGet("/report-settings", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, ReportDeliverySettingsService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.GetLedgerSettingsAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));
        ledger.MapPut("/report-settings", async (Guid orgId, Guid ledgerId, SettingsRequest body, PrincipalAccessor principals, ReportDeliverySettingsService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveLedgerSettingsAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId,
                new ReportSettingsInput(body.Language ?? "en", body.Comparative ?? "prior_period", body.ShowZeroBalances ?? true,
                    body.ShowAccountCodes ?? true, body.PaperSize ?? "A4", body.Orientation ?? "portrait", body.Watermark ?? "none",
                    body.PackSections ?? "pl,bs,tb,gl,aging", body.ShareDays ?? 7), ct)));
    }
}
