using Books.Application.DTOs;
using Microsoft.JSInterop;

namespace Books.Blazor.Services;

public class ReportDownloadService(IJSRuntime js)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfContentType = "application/pdf";

    public async Task DownloadAsync(ReportExportResult report)
    {
        await js.InvokeVoidAsync("downloadFileFromBytes", report.FileName, report.ContentType, Convert.ToBase64String(report.Content));
    }
}
