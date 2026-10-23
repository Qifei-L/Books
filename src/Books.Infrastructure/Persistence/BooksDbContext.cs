using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Infrastructure.Persistence;

public sealed class BooksDbContext(DbContextOptions<BooksDbContext> options) : DbContext(options), IBooksDb
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> Identities => Set<UserIdentity>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<LedgerGrant> LedgerGrants => Set<LedgerGrant>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BooksDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored as snake_case text guarded by CHECK constraints, so SQL stays readable.
        configurationBuilder.Properties<UserStatus>().HaveConversion<LowerCaseEnumConverter<UserStatus>>().HaveMaxLength(20);
        configurationBuilder.Properties<OrganizationState>().HaveConversion<LowerCaseEnumConverter<OrganizationState>>().HaveMaxLength(20);
        configurationBuilder.Properties<MembershipRole>().HaveConversion<LowerCaseEnumConverter<MembershipRole>>().HaveMaxLength(20);
        configurationBuilder.Properties<LedgerAccess>().HaveConversion<LowerCaseEnumConverter<LedgerAccess>>().HaveMaxLength(10);
        configurationBuilder.Properties<MembershipStatus>().HaveConversion<LowerCaseEnumConverter<MembershipStatus>>().HaveMaxLength(20);
    }
}
