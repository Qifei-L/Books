using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reporting;

public sealed record BrandingInput(string? LegalName, string? LegalNameKm, string? VatTin, string? Address, string? Phone,
    string? Email, string? BrandColor, string? Footer, string? PreparedBy, string? ReviewedBy);

public sealed record BrandingDto(string? LegalName, string? LegalNameKm, string? VatTin, string? Address, string? Phone,
    string? Email, string? BrandColor, string? Footer, string? PreparedBy, string? ReviewedBy, string Version);

public sealed record ReportSettingsInput(string Language, string Comparative, bool ShowZeroBalances, bool ShowAccountCodes,
    string PaperSize, string Orientation, string Watermark, string PackSections, int ShareDays);

public sealed record ReportSettingsDto(string Language, string Comparative, bool ShowZeroBalances, bool ShowAccountCodes,
    string PaperSize, string Orientation, string Watermark, string PackSections, int ShareDays, string Version);

/// <summary>Branding and print defaults for client report packs and tax workpapers.</summary>
public sealed class ReportDeliverySettingsService(IBooksDb db, ReadRunner reads, WriteRunner writes)
{
    public Task<BrandingDto> GetBrandingAsync(Principal principal, Guid orgId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            scope.RequireMember();
            var row = await db.OrganizationBranding.AsNoTracking().SingleOrDefaultAsync(x => x.OrgId == orgId, ct);
            return row is null ? DefaultBranding() : ToDto(row);
        }, ct);

    public Task<WriteResult> SaveBrandingAsync(Principal principal, WriteMeta meta, Guid orgId, BrandingInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("branding.save", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            var row = await db.OrganizationBranding.SingleOrDefaultAsync(x => x.OrgId == orgId, ct);
            var created = row is null;
            if (row is null)
            {
                row = new OrganizationBranding(orgId, scope.Now);
                db.OrganizationBranding.Add(row);
            }
            else
            {
                scope.CheckVersion(row.Version);
            }
            try
            {
                row.Set(input.LegalName, input.LegalNameKm, input.VatTin, input.Address, input.Phone, input.Email, input.BrandColor,
                    input.Footer, input.PreparedBy, input.ReviewedBy, scope.Now);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            scope.Audit(created ? "branding.created" : "branding.updated", "organization_branding", orgId, ToDto(row), orgId);
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(created ? 201 : 200, ToDto(row), row.Version);
        }, ct);

    public Task<ReportSettingsDto> GetLedgerSettingsAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var row = await db.LedgerReportSettings.AsNoTracking().SingleOrDefaultAsync(x => x.LedgerId == ledgerId, ct);
            return row is null ? DefaultSettings() : ToDto(row);
        }, ct);

    public Task<WriteResult> SaveLedgerSettingsAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, ReportSettingsInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("report_settings.save", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var row = await db.LedgerReportSettings.SingleOrDefaultAsync(x => x.LedgerId == ledgerId, ct);
            var created = row is null;
            if (row is null)
            {
                row = new LedgerReportSettings(orgId, ledgerId, scope.Now);
                db.LedgerReportSettings.Add(row);
            }
            else
            {
                scope.CheckVersion(row.Version);
            }
            try
            {
                row.Set(input.Language, input.Comparative, input.ShowZeroBalances, input.ShowAccountCodes, input.PaperSize,
                    input.Orientation, input.Watermark, input.PackSections, input.ShareDays, scope.Now);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            scope.Audit(created ? "report_settings.created" : "report_settings.updated", "ledger_report_settings", ledgerId, ToDto(row), orgId);
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(created ? 201 : 200, ToDto(row), row.Version);
        }, ct);

    private static BrandingDto DefaultBranding() => new(null, null, null, null, null, null, null, null, null, null, "0");

    private static BrandingDto ToDto(OrganizationBranding b) => new(b.LegalName, b.LegalNameKm, b.VatTin, b.Address, b.Phone, b.Email,
        b.BrandColor, b.Footer, b.PreparedBy, b.ReviewedBy, b.Version.ToString());

    private static ReportSettingsDto DefaultSettings() => new("en", "prior_period", true, true, "A4", "portrait", "none", "pl,bs,tb,gl,aging", 7, "0");

    private static ReportSettingsDto ToDto(LedgerReportSettings s) => new(s.Language, s.Comparative, s.ShowZeroBalances, s.ShowAccountCodes,
        s.PaperSize, s.Orientation, s.Watermark, s.PackSections, s.ShareDays, s.Version.ToString());
}
