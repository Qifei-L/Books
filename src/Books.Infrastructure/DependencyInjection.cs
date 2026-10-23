using Books.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Books.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBooksInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BooksDbContext>(options => Configure(options, connectionString));
        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(BooksDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention();
}
