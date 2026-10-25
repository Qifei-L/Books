using System.Globalization;
using Books.Application.Reporting;
using Books.Application.Documents;
using Stimulsoft.Base.Drawing;
using Stimulsoft.Report;
using Stimulsoft.Report.Components;

namespace Books.Api.Reporting;

/// <summary>Stimulsoft PDF workpapers. The tax workpaper is deliberately not an official GDT form facsimile.</summary>
public static class StimulsoftReportRenderer
{
    public static byte[] RenderTaxWorkpaper(TaxReturnDto dto, BrandingDto branding, string companyName)
    {
        var report = new StiReport();
        var page = NewPage(report);
        var y = 1.4;
        Add(page, 1.5, y, 18, 1, $"{companyName} · {dto.ReturnName}", 14);
        y += 0.9;
        Add(page, 1.5, y, 18, 0.6, $"Period {dto.Period} · {dto.Currency}" + (dto.TaxCurrency is null ? "" : $" / {dto.TaxCurrency}"), 9);
        y += 0.55;
        Add(page, 1.5, y, 18, 0.6, $"Tax workpaper · generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm} · not an official GDT form", 8);
        y += 0.9;

        Add(page, 1.5, y, 3, 0.5, "Box", 9);
        Add(page, 4.5, y, 9, 0.5, "Description", 9);
        Add(page, 14.5, y, 4.5, 0.5, $"Amount ({dto.Currency})", 9);
        y += 0.55;
        foreach (var box in dto.Boxes)
        {
            if (y > 26.5) { page = NewPage(report); y = 1.4; }
            Add(page, 1.5, y, 2.8, 0.5, box.Key, 8);
            Add(page, 4.5, y, 9.5, 0.5, box.Label, 8);
            Add(page, 14.5, y, 4.5, 0.5, box.Amount, 8, right: true);
            y += 0.52;
        }

        y += 0.5;
        if (y > 23) { page = NewPage(report); y = 1.4; }
        Add(page, 1.5, y, 18, 0.6, "Reconciliation with tax accounts", 11);
        y += 0.6;
        Add(page, 1.5, y, 5, 0.5, "Usage", 8);
        Add(page, 6.5, y, 4, 0.5, "Ledger", 8);
        Add(page, 10.5, y, 4, 0.5, "Return", 8);
        Add(page, 14.5, y, 4.5, 0.5, "Difference", 8);
        y += 0.52;
        foreach (var row in dto.Reconciliation)
        {
            if (y > 26.5) { page = NewPage(report); y = 1.4; }
            Add(page, 1.5, y, 4.8, 0.5, row.Label, 8);
            Add(page, 6.5, y, 3.8, 0.5, row.LedgerAmount, 8, right: true);
            Add(page, 10.5, y, 3.8, 0.5, row.ReturnAmount, 8, right: true);
            Add(page, 14.5, y, 4.5, 0.5, row.Difference, 8, right: true);
            y += 0.52;
        }

        if (dto.Issues.Count > 0)
        {
            y += 0.4;
            if (y > 24) { page = NewPage(report); y = 1.4; }
            Add(page, 1.5, y, 18, 0.5, "Issues", 10);
            y += 0.5;
            foreach (var issue in dto.Issues)
            {
                if (y > 26.5) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 18, 0.5, $"{issue.Code}: {issue.Message}", 7);
                y += 0.45;
            }
        }

        y += 0.8;
        Add(page, 1.5, y, 8, 0.5, $"Prepared by: {branding.PreparedBy ?? ""}", 8);
        Add(page, 11, y, 8, 0.5, $"Reviewed by: {branding.ReviewedBy ?? ""}", 8);
        y += 0.7;
        Add(page, 1.5, y, 18, 0.5, branding.Footer ?? "", 7);

        report.Render();
        using var stream = new MemoryStream();
        report.ExportDocument(StiExportFormat.Pdf, stream);
        return stream.ToArray();
    }

    private static StiPage NewPage(StiReport report)
    {
        var page = new StiPage(report)
        {
            PageWidth = 21.0,
            PageHeight = 29.7,
            Orientation = StiPageOrientation.Portrait,
            Margins = new StiMargins(1, 1, 1, 1),
        };
        report.Pages.Add(page);
        return page;
    }

    private static void Add(StiPage page, double left, double top, double width, double height, string text, int size, bool right = false)
    {
        var component = new StiText(new RectangleD(left, top, width, height), text)
        {
            HorAlignment = right ? StiTextHorAlignment.Right : StiTextHorAlignment.Left,
        };
        page.Components.Add(component);
    }
}
