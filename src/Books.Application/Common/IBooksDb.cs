using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Books.Application.Common;

public interface IBooksDb
{
    DbSet<User> Users { get; }
    DbSet<UserIdentity> Identities { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<Membership> Memberships { get; }
    DbSet<LedgerGrant> LedgerGrants { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<InvitationGrant> InvitationGrants { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<Company> Companies { get; }
    DbSet<Ledger> Ledgers { get; }
    DbSet<Account> Accounts { get; }
    DbSet<AccountGroup> AccountGroups { get; }
    DbSet<AccountPurpose> AccountPurposes { get; }
    DbSet<Journal> Journals { get; }
    DbSet<JournalRevision> JournalRevisions { get; }
    DbSet<JournalLine> JournalLines { get; }
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
