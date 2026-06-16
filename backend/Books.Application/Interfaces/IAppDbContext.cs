using Books.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<Entity> Entities { get; }
    DbSet<Ledger> Ledgers { get; }
    DbSet<Account> Accounts { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalLine> JournalLines { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
