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


    public static byte[] RenderClientPack(ProfitAndLoss pl, BalanceSheet bs, TrialBalance tb, ArAging ar, ApAging ap,
        BrandingDto branding, ReportSettingsDto settings, string companyName)
    {
        var report = new StiReport();
        var page = NewPage(report);
        var name = branding.LegalName ?? companyName;
        Add(page, 2, 3, 17, 1.2, name, 20);
        Add(page, 2, 4.4, 17, 0.8, "Client report pack", 16);
        Add(page, 2, 5.5, 17, 0.6, $"{pl.From:yyyy-MM-dd} to {pl.To:yyyy-MM-dd}", 10);
        Add(page, 2, 6.2, 17, 0.6, $"Currency {pl.Currency} · generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm}", 9);
        Add(page, 2, 12, 17, 0.6, $"Prepared by: {branding.PreparedBy ?? ""}", 9);
        Add(page, 2, 12.8, 17, 0.6, $"Reviewed by: {branding.ReviewedBy ?? ""}", 9);
        Add(page, 2, 26, 17, 0.5, branding.Footer ?? "", 7);

        if (settings.PackSections.Contains("pl"))
        {
            page = NewPage(report);
            Add(page, 1.5, 1.4, 18, 0.8, "Profit and loss", 14);
            Add(page, 1.5, 2.2, 18, 0.5, $"{pl.From:yyyy-MM-dd} to {pl.To:yyyy-MM-dd}", 8);
            var y = 2.9;
            foreach (var section in pl.Sections)
            {
                if (y > 25) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 18, 0.5, section.Title, 10);
                y += 0.45;
                foreach (var line in section.Lines)
                {
                    if (y > 26) { page = NewPage(report); y = 1.4; }
                    Add(page, 2.5, y, 11, 0.5, line.Name, 8);
                    Add(page, 14, y, 5, 0.5, line.Amount, 8, right: true);
                    y += 0.42;
                }
                Add(page, 2.5, y, 11, 0.5, $"Total {section.Title}", 8);
                Add(page, 14, y, 5, 0.5, section.Total, 8, right: true);
                y += 0.6;
            }
            Add(page, 2.5, y, 11, 0.5, "Net profit", 9);
            Add(page, 14, y, 5, 0.5, pl.NetProfit, 9, right: true);
        }

        if (settings.PackSections.Contains("bs"))
        {
            page = NewPage(report);
            Add(page, 1.5, 1.4, 18, 0.8, "Balance sheet", 14);
            Add(page, 1.5, 2.2, 18, 0.5, $"As of {bs.AsOf:yyyy-MM-dd}", 8);
            var y = 2.9;
            foreach (var section in bs.Sections)
            {
                if (y > 25) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 18, 0.5, section.Title, 10);
                y += 0.45;
                foreach (var line in section.Lines)
                {
                    if (y > 26) { page = NewPage(report); y = 1.4; }
                    Add(page, 2.5, y, 11, 0.5, line.Name, 8);
                    Add(page, 14, y, 5, 0.5, line.Amount, 8, right: true);
                    y += 0.42;
                }
                Add(page, 2.5, y, 11, 0.5, $"Total {section.Title}", 8);
                Add(page, 14, y, 5, 0.5, section.Total, 8, right: true);
                y += 0.6;
            }
            Add(page, 2.5, y, 11, 0.5, "Assets = liabilities and equity", 9);
            Add(page, 14, y, 5, 0.5, bs.TotalAssets == bs.TotalLiabilitiesAndEquity ? "Balanced" : "Not balanced", 9, right: true);
        }

        if (settings.PackSections.Contains("tb"))
        {
            page = NewPage(report);
            Add(page, 1.5, 1.4, 18, 0.8, "Trial balance", 14);
            Add(page, 1.5, 2.2, 18, 0.5, $"{tb.From:yyyy-MM-dd} to {tb.To:yyyy-MM-dd}", 8);
            var y = 2.9;
            Add(page, 1.5, y, 3, 0.4, "Code", 8);
            Add(page, 4.5, y, 8, 0.4, "Account", 8);
            Add(page, 13, y, 3, 0.4, "Debit", 8, right: true);
            Add(page, 16, y, 3, 0.4, "Credit", 8, right: true);
            y += 0.45;
            foreach (var row in tb.Rows)
            {
                if (y > 26) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 2.8, 0.4, row.Code ?? "", 7);
                Add(page, 4.5, y, 8.2, 0.4, row.Name, 7);
                Add(page, 13, y, 3, 0.4, row.ClosingDebit, 7, right: true);
                Add(page, 16, y, 3, 0.4, row.ClosingCredit, 7, right: true);
                y += 0.4;
            }
        }

        if (settings.PackSections.Contains("aging"))
        {
            page = NewPage(report);
            Add(page, 1.5, 1.4, 18, 0.8, "Receivables and payables aging", 14);
            Add(page, 1.5, 2.2, 18, 0.5, $"As of {ar.AsOf:yyyy-MM-dd}", 8);
            var y = 2.9;
            Add(page, 1.5, y, 6, 0.4, $"Receivables · {ar.Currency}", 9);
            y += 0.45;
            foreach (var row in ar.Rows)
            {
                if (y > 26) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 8, 0.4, row.Customer, 7);
                Add(page, 10, y, 3, 0.4, row.Total, 7, right: true);
                y += 0.4;
            }
            Add(page, 1.5, y, 8, 0.4, "Receivables total", 8);
            Add(page, 10, y, 3, 0.4, ar.Totals.Total, 8, right: true);
            y += 0.8;
            Add(page, 1.5, y, 6, 0.4, $"Payables · {ap.Currency}", 9);
            y += 0.45;
            foreach (var row in ap.Rows)
            {
                if (y > 26) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 8, 0.4, row.Supplier, 7);
                Add(page, 10, y, 3, 0.4, row.Total, 7, right: true);
                y += 0.4;
            }
            Add(page, 1.5, y, 8, 0.4, "Payables total", 8);
            Add(page, 10, y, 3, 0.4, ap.Totals.Total, 8, right: true);
        }

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
