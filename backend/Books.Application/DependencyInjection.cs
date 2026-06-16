using Books.Application.Services;
using Books.Application.Reports;
using Microsoft.Extensions.DependencyInjection;

namespace Books.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<JournalService>();
        services.AddScoped<ReportService>();
        services.AddScoped<GeneralLedgerAppService>();
        return services;
    }
}
