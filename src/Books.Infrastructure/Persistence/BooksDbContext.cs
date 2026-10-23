using Books.Application.Common;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Infrastructure.Persistence;

public sealed class BooksDbContext(DbContextOptions<BooksDbContext> options) : DbContext(options), IBooksDb
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> Identities => Set<UserIdentity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BooksDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as snake_case text guarded by CHECK constraints, so SQL stays readable.
        configurationBuilder.Properties<UserStatus>().HaveConversion<LowerCaseEnumConverter<UserStatus>>();
    }
}
