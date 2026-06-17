using Books.Application.Services;
using Books.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Books.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<JournalService>();
        services.AddTransient<ReportService>();
        services.AddTransient<ReportExportService>();
        services.AddTransient<GeneralLedgerAppService>();
        return services;
    }
}
