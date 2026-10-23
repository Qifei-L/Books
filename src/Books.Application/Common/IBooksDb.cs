using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Books.Application.Common;

public interface IBooksDb
{
    DbSet<User> Users { get; }
    DbSet<UserIdentity> Identities { get; }
    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
