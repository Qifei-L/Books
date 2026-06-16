using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Books.Blazor.Services;

public class ReportDownloadService(IHttpClientFactory httpClientFactory, IJSRuntime js, NavigationManager navigation)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string PdfContentType = "application/pdf";

    public async Task DownloadAsync(string endpoint, string fileName, string contentType)
    {
        var httpClient = httpClientFactory.CreateClient();
        var url = navigation.ToAbsoluteUri(endpoint);
        using var response = await httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"Download failed with HTTP {(int)response.StatusCode}."
                : detail);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        await js.InvokeVoidAsync("downloadFileFromBytes", fileName, contentType, Convert.ToBase64String(bytes));
    }
}
