using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record AccountPurposeDto(string Purpose, Guid? AccountId);

/// <summary>Which account serves each system purpose (rule 17). Part of ledger setup, so it uses the ledger version.</summary>
public sealed class AccountPurposeService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<AccountPurposeDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, ct);
        }, ct);

    /// <summary>Assigns an account to a purpose, or clears it with null.</summary>
    public Task<WriteResult> AssignAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, SystemPurpose purpose, Guid? accountId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("ledger.purpose", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            scope.CheckVersion(ledger.Version);
            var current = await db.AccountPurposes.SingleOrDefaultAsync(p => p.LedgerId == ledgerId && p.Purpose == purpose, ct);
            var before = current?.AccountId;
            if (accountId is null)
            {
                if (current is not null)
                {
                    db.AccountPurposes.Remove(current);
                }
            }
            else
            {
                var account = await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == accountId, ct)
                    ?? throw BooksProblem.Invalid("account_id must be an account in this ledger.");
                if (current is null)
                {
                    db.AccountPurposes.Add(new AccountPurpose(ledger, purpose, account));
                }
                else
                {
                    current.Assign(account);
                }
            }
            ledger.Touch();
            scope.Audit("ledger.purpose_assigned", "ledger", ledgerId, new { purpose = Text.Of(purpose), before, after = accountId });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, await LoadAsync(ledgerId, ct), ledger.Version);
        }, ct);

    private async Task<IReadOnlyList<AccountPurposeDto>> LoadAsync(Guid ledgerId, CancellationToken ct)
    {
        var assigned = await db.AccountPurposes.AsNoTracking().Where(p => p.LedgerId == ledgerId).ToDictionaryAsync(p => p.Purpose, p => p.AccountId, ct);
        return Enum.GetValues<SystemPurpose>()
            .Select(p => new AccountPurposeDto(Text.Of(p), assigned.TryGetValue(p, out var id) ? id : null))
            .ToList();
    }
}
