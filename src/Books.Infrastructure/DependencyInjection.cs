using Books.Application.Access;
using Books.Application.Common;
using Books.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Books.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBooksInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BooksDbContext>(options => Configure(options, connectionString));
        services.AddScoped<IBooksDb>(sp => sp.GetRequiredService<BooksDbContext>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IdentityService>();
        services.AddScoped<WriteRunner>();
        services.AddScoped<OrganizationService>();
        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(BooksDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();
}
