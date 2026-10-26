using Books.Application.Documents;
using Books.Application.Reporting;
using Books.Domain.Reporting;

namespace Books.Api.Reporting;

/// <summary>The data a client pack was rendered from, kept with the run so a shared copy has the same figures.</summary>
public sealed record ClientPackSnapshot(ProfitAndLoss Pl, BalanceSheet Bs, TrialBalance Tb, ArAging Ar, ApAging Ap, BrandingDto Branding,
    ReportSettingsDto Settings, string Company, IReadOnlyList<string> Sections);

/// <summary>The data a tax workpaper was rendered from.</summary>
public sealed record TaxWorkpaperSnapshot(TaxReturnDto Return, BrandingDto Branding, string Company, IReadOnlyList<string> Sections);

/// <summary>Re-renders a stored run for a share link, adding the shared-copy watermark without touching the books.</summary>
public static class ReportSnapshots
{
    public static byte[]? WithWatermark(ReportRun run)
    {
        var options = Books.Application.Common.BooksJson.Options;
        switch (run.Kind)
        {
            case "client_pack":
                var pack = System.Text.Json.JsonSerializer.Deserialize<ClientPackSnapshot>(run.Snapshot, options);
                return pack is null ? null : StimulsoftReportRenderer.RenderClientPack(pack.Pl, pack.Bs, pack.Tb, pack.Ar, pack.Ap,
                    pack.Branding, pack.Settings, pack.Company, pack.Sections, $"Shared copy · {run.Period}");
            case "tax_workpaper":
            case "tax_return":
                var workpaper = System.Text.Json.JsonSerializer.Deserialize<TaxWorkpaperSnapshot>(run.Snapshot, options);
                return workpaper is null ? null : StimulsoftReportRenderer.RenderTaxWorkpaper(workpaper.Return, workpaper.Branding,
                    workpaper.Company, workpaper.Sections, $"Shared copy · {run.Period}");
            default:
                return null;
        }
    }
}
