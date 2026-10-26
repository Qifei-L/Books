using Books.Application.Reporting;
using Books.Application.Documents;
using Stimulsoft.Base.Drawing;
using Stimulsoft.Report;
using Stimulsoft.Report.Components;
using Color = System.Drawing.Color;

namespace Books.Api.Reporting;

/// <summary>
/// Stimulsoft PDF workpapers. The tax workpaper is deliberately not an official GDT form facsimile. Section order and
/// on/off switches come from the ledger's report template; the defaults are used when no template is published.
/// </summary>
public static class StimulsoftReportRenderer
{
    public static byte[] RenderTaxWorkpaper(TaxReturnDto dto, BrandingDto branding, string companyName, IReadOnlyList<string>? sections = null,
        string? watermark = null)
    {
        var enabled = (sections ?? ["boxes", "reconciliation", "state_charge", "non_deductible", "issues", "signatures"]).ToHashSet();
        var report = new StiReport();
        var page = NewPage(report);
        var y = 1.4;
        Add(page, 1.5, y, 18, 1, $"{companyName} · {dto.ReturnName}", 14);
        y += 0.9;
        Add(page, 1.5, y, 18, 0.6, $"Period {dto.Period} · {dto.Currency}" + (dto.TaxCurrency is null ? "" : $" / {dto.TaxCurrency}"), 9);
        y += 0.55;
        Add(page, 1.5, y, 18, 0.6, $"Tax workpaper · generated {DateTimeOffset.Now:yyyy-MM-dd HH:mm} · not an official GDT form", 8);
        y += 0.9;

        if (enabled.Contains("boxes"))
        {
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
        }

        if (enabled.Contains("reconciliation"))
        {
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
        }

        if (enabled.Contains("state_charge") && dto.StateChargeLines is { Count: > 0 })
        {
            y += 0.5;
            if (y > 24) { page = NewPage(report); y = 1.4; }
            Add(page, 1.5, y, 18, 0.6, "State charge VAT (ordinary output VAT)", 11);
            y += 0.55;
            Add(page, 1.5, y, 4.5, 0.5, "Document", 8);
            Add(page, 6, y, 2.5, 0.5, "Date", 8);
            Add(page, 8.5, y, 5, 0.5, "Party", 8);
            Add(page, 14, y, 2.5, 0.5, "Net", 8, right: true);
            Add(page, 16.5, y, 3, 0.5, "VAT", 8, right: true);
            y += 0.52;
            foreach (var line in dto.StateChargeLines)
            {
                if (y > 26.5) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 4.4, 0.5, $"{line.DocumentType} {line.DocumentNo}", 7);
                Add(page, 6, y, 2.4, 0.5, line.Date.ToString("yyyy-MM-dd"), 7);
                Add(page, 8.5, y, 5, 0.5, line.Party, 7);
                Add(page, 14, y, 2.5, 0.5, line.Net, 7, right: true);
                Add(page, 16.5, y, 3, 0.5, line.Tax, 7, right: true);
                y += 0.45;
            }
        }

        if (enabled.Contains("non_deductible") && dto.NonDeductibleLines is { Count: > 0 })
        {
            y += 0.5;
            if (y > 23) { page = NewPage(report); y = 1.4; }
            Add(page, 1.5, y, 18, 0.6, "Non-creditable input VAT", 11);
            y += 0.55;
            Add(page, 1.5, y, 4.5, 0.5, "Document", 8);
            Add(page, 6, y, 2.5, 0.5, "Date", 8);
            Add(page, 8.5, y, 4, 0.5, "Party", 8);
            Add(page, 12.5, y, 1.2, 0.5, "Box", 8);
            Add(page, 14, y, 2.5, 0.5, "Net", 8, right: true);
            Add(page, 16.5, y, 3, 0.5, "VAT", 8, right: true);
            y += 0.52;
            foreach (var line in dto.NonDeductibleLines)
            {
                if (y > 26.5) { page = NewPage(report); y = 1.4; }
                Add(page, 1.5, y, 4.4, 0.5, $"{line.DocumentType} {line.DocumentNo}", 7);
                Add(page, 6, y, 2.4, 0.5, line.Date.ToString("yyyy-MM-dd"), 7);
                Add(page, 8.5, y, 3.9, 0.5, line.Party, 7);
                Add(page, 12.5, y, 1.2, 0.5, line.Box ?? "", 7);
                Add(page, 14, y, 2.5, 0.5, line.Net, 7, right: true);
                Add(page, 16.5, y, 3, 0.5, line.Tax, 7, right: true);
                y += 0.45;
            }
        }

        if (enabled.Contains("issues") && dto.Issues.Count > 0)
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

        if (enabled.Contains("signatures"))
        {
            y += 0.8;
            if (y > 26) { page = NewPage(report); y = 1.4; }
            Add(page, 1.5, y, 8, 0.5, $"Prepared by: {branding.PreparedBy ?? ""}", 8);
            Add(page, 11, y, 8, 0.5, $"Reviewed by: {branding.ReviewedBy ?? ""}", 8);
            y += 0.7;
            Add(page, 1.5, y, 18, 0.5, branding.Footer ?? "", 7);
        }

        Stamp(report, watermark);
        report.Render();
        using var stream = new MemoryStream();
        report.ExportDocument(StiExportFormat.Pdf, stream);
        return stream.ToArray();
    }

    /// <summary>Sections are drawn in the template's order, so a ledger can put the balance sheet before the P&amp;L.</summary>
    public static byte[] RenderClientPack(ProfitAndLoss pl, BalanceSheet bs, TrialBalance tb, ArAging ar, ApAging ap,
        BrandingDto branding, ReportSettingsDto settings, string companyName, IReadOnlyList<string>? sections = null, string? watermark = null)
    {
        var order = sections ?? settings.PackSections.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

        foreach (var section in order.Select(s => s.ToLowerInvariant()).Distinct())
        {
            switch (section)
            {
                case "pl": AddProfitAndLoss(report, pl); break;
                case "bs": AddBalanceSheet(report, bs); break;
                case "tb": AddTrialBalance(report, tb); break;
                case "aging": AddAging(report, ar, ap); break;
            }
        }

        Stamp(report, watermark);
        report.Render();
        using var stream = new MemoryStream();
        report.ExportDocument(StiExportFormat.Pdf, stream);
        return stream.ToArray();
    }

    private static void AddProfitAndLoss(StiReport report, ProfitAndLoss pl)
    {
        var page = NewPage(report);
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

    private static void AddBalanceSheet(StiReport report, BalanceSheet bs)
    {
        var page = NewPage(report);
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

    private static void AddTrialBalance(StiReport report, TrialBalance tb)
    {
        var page = NewPage(report);
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

    private static void AddAging(StiReport report, ArAging ar, ApAging ap)
    {
        var page = NewPage(report);
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

    /// <summary>A visible mark on every shared page, so a link's copy is recognizable as shared.</summary>
    private static void Stamp(StiReport report, string? watermark)
    {
        if (string.IsNullOrWhiteSpace(watermark))
        {
            return;
        }
        foreach (var page in report.Pages.OfType<StiPage>())
        {
            Add(page, 1.5, 1.0, 18, 0.5, watermark, 7, color: Color.FromArgb(150, 150, 150), center: true);
            Add(page, 1.5, 28.2, 18, 0.5, watermark, 7, color: Color.FromArgb(150, 150, 150), center: true);
            Add(page, 2, 13, 17, 3, watermark, 28, color: Color.FromArgb(190, 190, 190), center: true);
        }
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

    private static void Add(StiPage page, double left, double top, double width, double height, string text, int size, bool right = false,
        Color? color = null, bool center = false)
    {
        var component = new StiText(new RectangleD(left, top, width, height), text)
        {
            HorAlignment = center ? StiTextHorAlignment.Center : right ? StiTextHorAlignment.Right : StiTextHorAlignment.Left,
        };
        if (color is { } brush)
        {
            component.TextBrush = new StiSolidBrush(brush);
        }
        page.Components.Add(component);
    }
}
