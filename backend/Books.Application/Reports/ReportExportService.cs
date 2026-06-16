using Books.Application.DTOs;
using Books.Application.Interfaces;
using Books.Domain.Entities;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Books.Application.Reports;

public class ReportExportService(IAppDbContext db, ReportService reports)
{
    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string PdfContentType = "application/pdf";
    private const string MoneyFormat = "#,##0.00";

    public async Task<ReportExportResult> ExportTrialBalanceExcelAsync(int ledgerId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetTrialBalanceAsync(ledgerId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var period = PeriodLabel(dateFrom, dateTo);
        var bytes = BuildExcel("Trial Balance", ledger.Name, period, ["Account", "Name", "Debit", "Credit"], rows.Select(row => new object[]
        {
            row.AccountCode,
            row.AccountName,
            row.Debit,
            row.Credit,
        }));

        return new ReportExportResult(bytes, $"trial-balance-{FilePeriod(dateTo ?? DateTime.Today)}.xlsx", ExcelContentType);
    }

    public async Task<ReportExportResult> ExportTrialBalancePdfAsync(int ledgerId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetTrialBalanceAsync(ledgerId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var period = PeriodLabel(dateFrom, dateTo);
        var bytes = BuildPdf("Trial Balance", ledger.Name, period, ["Account", "Name", "Debit", "Credit"], rows.Select(row => new string[]
        {
            row.AccountCode,
            row.AccountName,
            Money(row.Debit),
            Money(row.Credit),
        }));

        return new ReportExportResult(bytes, $"trial-balance-{FilePeriod(dateTo ?? DateTime.Today)}.pdf", PdfContentType);
    }

    public async Task<ReportExportResult> ExportGeneralLedgerExcelAsync(int ledgerId, int accountId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetGeneralLedgerAsync(ledgerId, accountId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var account = await GetAccountAsync(ledgerId, accountId);
        var period = PeriodLabel(dateFrom, dateTo);
        var bytes = BuildExcel("General Ledger", ledger.Name, period, ["Date", "Journal No", "Description", "Debit", "Credit", "Balance"], rows.Select(row => new object[]
        {
            row.EntryDate,
            row.JournalNo,
            row.Description,
            row.Debit,
            row.Credit,
            row.Balance,
        }));

        return new ReportExportResult(bytes, $"general-ledger-{Slug(account?.Name ?? "account")}-{FileMonth(dateTo ?? DateTime.Today)}.xlsx", ExcelContentType);
    }

    public async Task<ReportExportResult> ExportGeneralLedgerPdfAsync(int ledgerId, int accountId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetGeneralLedgerAsync(ledgerId, accountId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var account = await GetAccountAsync(ledgerId, accountId);
        var period = $"{PeriodLabel(dateFrom, dateTo)} | {account?.Code} {account?.Name}".Trim();
        var bytes = BuildPdf("General Ledger", ledger.Name, period, ["Date", "Journal No", "Description", "Debit", "Credit", "Balance"], rows.Select(row => new string[]
        {
            row.EntryDate.ToString("yyyy-MM-dd"),
            row.JournalNo,
            row.Description,
            Money(row.Debit),
            Money(row.Credit),
            Money(row.Balance),
        }));

        return new ReportExportResult(bytes, $"general-ledger-{Slug(account?.Name ?? "account")}-{FileMonth(dateTo ?? DateTime.Today)}.pdf", PdfContentType);
    }

    public async Task<ReportExportResult> ExportProfitLossExcelAsync(int ledgerId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetProfitLossAsync(ledgerId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var period = PeriodLabel(dateFrom, dateTo);
        var bytes = BuildExcel("Profit & Loss", ledger.Name, period, ["Account", "Name", "Type", "Debit", "Credit", "Balance"], rows.Select(row => new object[]
        {
            row.AccountCode,
            row.AccountName,
            row.AccountType.ToString(),
            row.Debit,
            row.Credit,
            row.Balance,
        }));

        return new ReportExportResult(bytes, $"profit-and-loss-{FileMonth(dateTo ?? DateTime.Today)}.xlsx", ExcelContentType);
    }

    public async Task<ReportExportResult> ExportProfitLossPdfAsync(int ledgerId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetProfitLossAsync(ledgerId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var period = PeriodLabel(dateFrom, dateTo);
        var bytes = BuildPdf("Profit & Loss", ledger.Name, period, ["Account", "Name", "Type", "Balance"], rows.Select(row => new string[]
        {
            row.AccountCode,
            row.AccountName,
            row.AccountType.ToString(),
            Money(row.Balance),
        }));

        return new ReportExportResult(bytes, $"profit-and-loss-{FileMonth(dateTo ?? DateTime.Today)}.pdf", PdfContentType);
    }

    public async Task<ReportExportResult> ExportBalanceSheetExcelAsync(int ledgerId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetBalanceSheetAsync(ledgerId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var period = PeriodLabel(dateFrom, dateTo);
        var bytes = BuildExcel("Balance Sheet", ledger.Name, period, ["Account", "Name", "Type", "Debit", "Credit", "Balance"], rows.Select(row => new object[]
        {
            row.AccountCode,
            row.AccountName,
            row.AccountType.ToString(),
            row.Debit,
            row.Credit,
            row.Balance,
        }));

        return new ReportExportResult(bytes, $"balance-sheet-{FilePeriod(dateTo ?? DateTime.Today)}.xlsx", ExcelContentType);
    }

    public async Task<ReportExportResult> ExportBalanceSheetPdfAsync(int ledgerId, DateTime? dateFrom, DateTime? dateTo)
    {
        var rows = await reports.GetBalanceSheetAsync(ledgerId, dateFrom, dateTo);
        var ledger = await GetLedgerAsync(ledgerId);
        var period = PeriodLabel(dateFrom, dateTo);
        var bytes = BuildPdf("Balance Sheet", ledger.Name, period, ["Account", "Name", "Type", "Balance"], rows.Select(row => new string[]
        {
            row.AccountCode,
            row.AccountName,
            row.AccountType.ToString(),
            Money(row.Balance),
        }));

        return new ReportExportResult(bytes, $"balance-sheet-{FilePeriod(dateTo ?? DateTime.Today)}.pdf", PdfContentType);
    }

    private static byte[] BuildExcel(string title, string ledgerName, string period, string[] headers, IEnumerable<object[]> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(title);
        for (var column = 0; column < headers.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = headers[column];
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                SetCellValue(worksheet.Cell(rowIndex, column + 1), row[column]);
            }
            rowIndex++;
        }

        worksheet.Cell(rowIndex + 1, 1).Value = "Books";
        worksheet.Cell(rowIndex + 1, 2).Value = title;
        worksheet.Cell(rowIndex + 2, 1).Value = "Ledger";
        worksheet.Cell(rowIndex + 2, 2).Value = ledgerName;
        worksheet.Cell(rowIndex + 3, 1).Value = "Period";
        worksheet.Cell(rowIndex + 3, 2).Value = period;
        worksheet.Cell(rowIndex + 4, 1).Value = "Generated";
        worksheet.Cell(rowIndex + 4, 2).Value = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        worksheet.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        worksheet.Range(1, 1, 1, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#E3EAED");
        worksheet.Range(rowIndex + 1, 1, rowIndex + 4, 2).Style.Font.FontColor = XLColor.Gray;
        worksheet.SheetView.FreezeRows(1);

        for (var column = 1; column <= headers.Length; column++)
        {
            var header = headers[column - 1];
            if (header is "Debit" or "Credit" or "Balance")
            {
                worksheet.Column(column).Style.NumberFormat.Format = MoneyFormat;
                worksheet.Column(column).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        worksheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] BuildPdf(string title, string ledgerName, string period, string[] headers, IEnumerable<string[]> rows)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var generatedAt = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        var rowList = rows.ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(text => text.FontSize(9));

                page.Header().Column(column =>
                {
                    column.Item().Text("Books").FontSize(10).FontColor(Colors.Grey.Darken1);
                    column.Item().Text(title).FontSize(18).Bold().FontColor(Colors.BlueGrey.Darken4);
                    column.Item().Text($"{ledgerName} | {period} | Generated {generatedAt}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    column.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingTop(14).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in headers)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    table.Header(header =>
                    {
                        foreach (var item in headers)
                        {
                            header.Cell().Element(HeaderCell).Text(item);
                        }
                    });

                    foreach (var row in rowList)
                    {
                        for (var i = 0; i < headers.Length; i++)
                        {
                            var value = i < row.Length ? row[i] : string.Empty;
                            var cell = table.Cell().Element(Cell);
                            if (IsMoneyHeader(headers[i]))
                            {
                                cell = cell.AlignRight();
                            }

                            cell.Text(value);
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();

        static IContainer HeaderCell(IContainer container) => container
            .Background(Colors.Grey.Lighten3)
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten1)
            .Padding(5)
            .DefaultTextStyle(text => text.Bold());

        static IContainer Cell(IContainer container) => container
            .BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten3)
            .Padding(5);
    }

    private async Task<Ledger> GetLedgerAsync(int ledgerId)
    {
        return await db.Ledgers.FirstOrDefaultAsync(x => x.Id == ledgerId)
            ?? new Ledger { Id = ledgerId, Code = $"L{ledgerId}", Name = $"Ledger {ledgerId}", IsActive = true };
    }

    private async Task<Account?> GetAccountAsync(int ledgerId, int accountId)
    {
        var entityId = await db.Ledgers
            .Where(x => x.Id == ledgerId)
            .Select(x => (int?)x.EntityId)
            .FirstOrDefaultAsync();

        return entityId.HasValue
            ? await db.Accounts.FirstOrDefaultAsync(x => x.Id == accountId && x.EntityId == entityId.Value)
            : null;
    }

    private static string PeriodLabel(DateTime? from, DateTime? to)
    {
        return (from, to) switch
        {
            ({ } start, { } end) => $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
            ({ } start, null) => $"From {start:yyyy-MM-dd}",
            (null, { } end) => $"Through {end:yyyy-MM-dd}",
            _ => "All periods"
        };
    }

    private static string Money(decimal value) => value.ToString("N2");
    private static string FilePeriod(DateTime date) => date.ToString("yyyy-MM-dd");
    private static string FileMonth(DateTime date) => date.ToString("yyyy-MM");
    private static bool IsMoneyHeader(string header) => header is "Debit" or "Credit" or "Balance";

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Value = string.Empty;
                break;
            case DateTime date:
                cell.Value = date;
                cell.Style.DateFormat.Format = "yyyy-mm-dd";
                break;
            case decimal number:
                cell.Value = number;
                break;
            case double number:
                cell.Value = number;
                break;
            case float number:
                cell.Value = number;
                break;
            case int number:
                cell.Value = number;
                break;
            case long number:
                cell.Value = number;
                break;
            default:
                cell.Value = value.ToString() ?? string.Empty;
                break;
        }
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
