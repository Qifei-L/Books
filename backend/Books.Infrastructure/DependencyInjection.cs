using Books.Application.Interfaces;
using Books.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Books.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = DatabaseConnection.GetDatabaseConnectionString(configuration);
        services.AddDbContext<AppDbContext>(
            options => options.UseNpgsql(connectionString),
            ServiceLifetime.Transient,
            ServiceLifetime.Transient);
        services.AddTransient<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        return services;
    }
}
