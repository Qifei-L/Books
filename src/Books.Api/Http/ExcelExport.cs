using System.Globalization;
using Books.Application.Documents;
using Books.Application.Reporting;
using ClosedXML.Excel;

namespace Books.Api.Http;

/// <summary>
/// Writes reports as .xlsx with real numeric cells, from the same objects the JSON endpoints return, so the
/// spreadsheet always agrees with the API. Formatting is deliberately plain.
/// </summary>
public static class ExcelExport
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] TrialBalance(TrialBalance tb, string ledgerName)
    {
        using var book = new XLWorkbook();
        var sheet = Sheet(book, "Trial Balance", ledgerName, $"{tb.From:yyyy-MM-dd} to {tb.To:yyyy-MM-dd} · {tb.Currency}");
        Header(sheet, 4, "Code", "Account", "Opening Dr", "Opening Cr", "Period Dr", "Period Cr", "Closing Dr", "Closing Cr");
        var row = 5;
        foreach (var r in tb.Rows)
        {
            sheet.Cell(row, 1).Value = r.Code ?? "";
            sheet.Cell(row, 2).Value = r.Name;
            Amounts(sheet, row, 3, r.OpeningDebit, r.OpeningCredit, r.PeriodDebit, r.PeriodCredit, r.ClosingDebit, r.ClosingCredit);
            row++;
        }
        var t = tb.Totals;
        sheet.Cell(row, 2).Value = "Total";
        Amounts(sheet, row, 3, t.OpeningDebit, t.OpeningCredit, t.PeriodDebit, t.PeriodCredit, t.ClosingDebit, t.ClosingCredit);
        sheet.Row(row).Style.Font.Bold = true;
        Notes(sheet, row + 2, tb.Warnings.Select(w => w.Message).Prepend(tb.Balanced ? "Balanced." : "NOT BALANCED."));
        return Save(book, sheet);
    }

    public static byte[] GlDetail(GlDetail gl, string ledgerName)
    {
        using var book = new XLWorkbook();
        var sheet = Sheet(book, "GL Detail", ledgerName, $"{gl.Code} {gl.Name} · {gl.From:yyyy-MM-dd} to {gl.To:yyyy-MM-dd} · {gl.Currency}");
        Header(sheet, 4, "Date", "Journal No.", "Reference", "Description", "Debit", "Credit", "Balance");
        sheet.Cell(5, 4).Value = "Opening balance";
        Amounts(sheet, 5, 7, gl.OpeningBalance);
        var row = 6;
        foreach (var l in gl.Lines)
        {
            sheet.Cell(row, 1).Value = l.Date.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            sheet.Cell(row, 2).Value = l.JournalNo;
            sheet.Cell(row, 3).Value = l.Reference ?? "";
            sheet.Cell(row, 4).Value = l.Description ?? "";
            Amounts(sheet, row, 5, l.Debit, l.Credit, l.Balance);
            row++;
        }
        sheet.Cell(row, 4).Value = "Period totals / closing balance";
        Amounts(sheet, row, 5, gl.PeriodDebit, gl.PeriodCredit, gl.ClosingBalance);
        sheet.Row(row).Style.Font.Bold = true;
        return Save(book, sheet);
    }

    public static byte[] ProfitAndLoss(ProfitAndLoss pl, string ledgerName)
    {
        using var book = new XLWorkbook();
        var sheet = Sheet(book, "Profit and Loss", ledgerName, $"{pl.From:yyyy-MM-dd} to {pl.To:yyyy-MM-dd} · {pl.Currency}");
        var row = Sections(sheet, 4, pl.Sections);
        sheet.Cell(row, 1).Value = "Net profit";
        Amounts(sheet, row, 2, pl.NetProfit);
        sheet.Row(row).Style.Font.Bold = true;
        return Save(book, sheet);
    }

    public static byte[] BalanceSheet(BalanceSheet bs, string ledgerName)
    {
        using var book = new XLWorkbook();
        var sheet = Sheet(book, "Balance Sheet", ledgerName, $"As of {bs.AsOf:yyyy-MM-dd} · {bs.Currency}");
        var row = Sections(sheet, 4, bs.Sections);
        sheet.Cell(row, 1).Value = "Total liabilities and equity";
        Amounts(sheet, row, 2, bs.TotalLiabilitiesAndEquity);
        sheet.Row(row).Style.Font.Bold = true;
        Notes(sheet, row + 2, [bs.Balanced ? "Balanced: assets equal liabilities plus equity." : "NOT BALANCED."]);
        return Save(book, sheet);
    }

    /// <summary>Each section, its nested groups indented with subtotals, then the section total. Returns the next free row.</summary>
    private static int Sections(IXLWorksheet sheet, int row, IReadOnlyList<StatementSection> sections)
    {
        foreach (var section in sections)
        {
            sheet.Cell(row, 1).Value = section.Title;
            sheet.Row(row).Style.Font.Bold = true;
            row++;
            foreach (var line in section.Lines)
            {
                row = Line(sheet, row, line, 1);
            }
            sheet.Cell(row, 1).Value = $"Total {section.Title}";
            Amounts(sheet, row, 2, section.Total);
            sheet.Row(row).Style.Font.Bold = true;
            row += 2;
        }
        return row;
    }

    private static int Line(IXLWorksheet sheet, int row, StatementLine line, int level)
    {
        var name = line.Code is null ? line.Name : $"{line.Code} {line.Name}";
        sheet.Cell(row, 1).Value = name;
        sheet.Cell(row, 1).Style.Alignment.Indent = level;
        if (line.Kind != "group")
        {
            Amounts(sheet, row, 2, line.Amount);
            return row + 1;
        }
        sheet.Cell(row, 1).Style.Font.Bold = true;
        row++;
        foreach (var child in line.Children)
        {
            row = Line(sheet, row, child, level + 1);
        }
        sheet.Cell(row, 1).Value = $"Total {line.Name}";
        sheet.Cell(row, 1).Style.Alignment.Indent = level;
        Amounts(sheet, row, 2, line.Amount);
        sheet.Row(row).Style.Font.Bold = true;
        return row + 1;
    }

    /// <summary>The monthly return workpaper: boxes, reconciliation with the tax accounts, and lines with an overridden VAT.</summary>
    public static byte[] TaxReturn(TaxReturnDto r, string ledgerName)
    {
        using var book = new XLWorkbook();
        var basis = r.TaxCurrencyBasis == "override" ? $"{r.TaxCurrency} at {r.OverrideRate} ({r.OverrideReason})" : $"{r.TaxCurrency} from document snapshots";
        var sheet = Sheet(book, "Tax return", ledgerName, $"{r.ReturnName} · {r.Period} · definition v{r.DefinitionVersion} · {r.Currency}, {basis}");
        Header(sheet, 4, "Section", "Box", "Label", r.Currency, r.TaxCurrency ?? "");
        var row = 5;
        foreach (var b in r.Boxes)
        {
            sheet.Cell(row, 1).Value = b.Section;
            sheet.Cell(row, 2).Value = b.Key;
            sheet.Cell(row, 3).Value = b.Information ? $"{b.Label} (information)" : b.Label;
            Amounts(sheet, row, 4, b.Amount);
            if (b.TaxCurrencyAmount is { } khr)
            {
                Amounts(sheet, row, 5, khr);
            }
            row++;
        }
        Notes(sheet, row + 1, r.Issues.Select(i => $"{i.Code}: {i.Message}")
            .Concat(r.Filed is { } f ? [$"Filed {f.FiledAt:yyyy-MM-dd HH:mm} UTC; {f.Differences.Count} boxes differ from the filed return."] : ["Not filed."]));

        var rec = book.Worksheets.Add("Reconciliation");
        Header(rec, 1, "Usage", "Accounts", "Ledger", "Documents", "Difference", "Other postings");
        row = 2;
        foreach (var x in r.Reconciliation)
        {
            rec.Cell(row, 1).Value = x.Label;
            rec.Cell(row, 2).Value = string.Join(", ", x.Accounts);
            Amounts(rec, row, 3, x.LedgerAmount, x.ReturnAmount, x.Difference);
            rec.Cell(row, 6).Value = string.Join("; ", x.OtherPostings.Select(o => $"No. {o.JournalNo} {o.Date:yyyy-MM-dd} {o.Reference} {o.Amount}"));
            row++;
        }
        rec.Columns().AdjustToContents(8, 60);

        var adjusted = book.Worksheets.Add("Adjusted VAT");
        Header(adjusted, 1, "Document", "No.", "Date", "Party", "Line", "Calculated", "Entered", "Reason");
        row = 2;
        foreach (var l in r.AdjustedLines)
        {
            adjusted.Cell(row, 1).Value = l.DocumentType;
            adjusted.Cell(row, 2).Value = l.DocumentNo;
            adjusted.Cell(row, 3).Value = l.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            adjusted.Cell(row, 4).Value = l.Party;
            adjusted.Cell(row, 5).Value = l.LineNo;
            Amounts(adjusted, row, 6, l.TaxCalculated ?? "0", l.TaxAmount ?? "0");
            adjusted.Cell(row, 8).Value = l.TaxReason ?? "";
            row++;
        }
        adjusted.Columns().AdjustToContents(8, 60);
        return Save(book, sheet);
    }

    private static IXLWorksheet Sheet(XLWorkbook book, string title, string ledgerName, string subtitle)
    {
        var sheet = book.Worksheets.Add(title);
        sheet.Cell(1, 1).Value = $"{ledgerName} — {title}";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 13;
        sheet.Cell(2, 1).Value = subtitle;
        return sheet;
    }

    private static void Header(IXLWorksheet sheet, int row, params string[] titles)
    {
        for (var i = 0; i < titles.Length; i++)
        {
            sheet.Cell(row, i + 1).Value = titles[i];
        }
        sheet.Row(row).Style.Font.Bold = true;
        sheet.Row(row).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
    }

    /// <summary>Writes decimal strings as numbers; empty cells for zero in debit/credit columns would hide data, so zeros stay.</summary>
    private static void Amounts(IXLWorksheet sheet, int row, int column, params string[] amounts)
    {
        for (var i = 0; i < amounts.Length; i++)
        {
            var cell = sheet.Cell(row, column + i);
            cell.Value = decimal.Parse(amounts[i], CultureInfo.InvariantCulture);
            var decimals = amounts[i].Contains('.') ? amounts[i].Length - amounts[i].IndexOf('.') - 1 : 0;
            cell.Style.NumberFormat.Format = decimals == 0 ? "#,##0;-#,##0" : "#,##0." + new string('0', decimals) + ";-#,##0." + new string('0', decimals);
        }
    }

    private static void Notes(IXLWorksheet sheet, int row, IEnumerable<string> notes)
    {
        foreach (var note in notes)
        {
            sheet.Cell(row++, 1).Value = note;
        }
    }

    private static byte[] Save(XLWorkbook book, IXLWorksheet sheet)
    {
        sheet.Columns().AdjustToContents(8, 60);
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
