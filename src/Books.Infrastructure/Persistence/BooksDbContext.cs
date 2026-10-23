using Microsoft.EntityFrameworkCore;

namespace Books.Infrastructure.Persistence;

public sealed class BooksDbContext(DbContextOptions<BooksDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BooksDbContext).Assembly);
    }
}
