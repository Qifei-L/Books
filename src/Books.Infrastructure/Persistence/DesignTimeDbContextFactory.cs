using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Books.Infrastructure.Persistence;

/// <summary>Used by dotnet-ef. Reads ConnectionStrings__Books; adding a migration needs no live database.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BooksDbContext>
{
    public BooksDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Books")
            ?? "Host=localhost;Database=books_design";
        var options = new DbContextOptionsBuilder<BooksDbContext>();
        DependencyInjection.Configure(options, connectionString);
        return new BooksDbContext(options.Options);
    }
}
