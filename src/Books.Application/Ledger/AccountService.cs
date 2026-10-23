using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record AccountDto(
    Guid Id, Guid LedgerId, Guid? GroupId, string Code, string Name, string Type, bool IsControl, bool IsBank,
    string? Currency, bool Active, bool Postable, bool Used, IReadOnlyList<string> Purposes, string Version);

/// <param name="GroupChanges">True when group_id was sent; GroupId null then means no group.</param>
public sealed record UpdateAccount(string? Code, string? Name, AccountType? Type, bool? Active, bool GroupChanges, Guid? GroupId);

public sealed record CreateAccount(string Code, string Name, AccountType Type, Guid? GroupId, bool IsControl, bool IsBank, string? Currency);

/// <summary>Accounts of a chart. Reading needs ledger read access; changes need edit access.</summary>
public sealed class AccountService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<AccountDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, CreateAccount command, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("account.create", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var group = await FindGroupAsync(ledgerId, command.GroupId, ct);
            var account = new Account(Guid.CreateVersion7(), ledger, command.Code, command.Name, command.Type, group,
                command.IsControl, command.IsBank, command.Currency, scope.Now);
            await EnsureCodeFreeAsync(ledgerId, account.Code, account.Id, ct);

            db.Accounts.Add(account);
            scope.Audit("account.created", "account", account.Id, new
            {
                ledger_id = ledgerId,
                code = account.Code,
                name = account.Name,
                type = Text.Of(account.Type),
                group_id = account.GroupId,
            });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, (await LoadAsync(ledgerId, account.Id, ct)).Single(), account.Version);
        }, ct);

    /// <summary>
    /// Partial update: omitted fields stay unchanged. The Id never changes, so journals keep pointing at the
    /// same account when its code changes (rule 16).
    /// </summary>
    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId, UpdateAccount command, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("account.update", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == accountId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(account.Version);
            var before = (await LoadAsync(ledgerId, account.Id, ct)).Single();

            if (command.Type is { } type && type != account.Type)
            {
                if (await IsUsedAsync(account.Id, ct))
                {
                    throw BooksProblem.Invalid("The type of an account used by journals cannot change (rule 19).");
                }
                var purposes = await db.AccountPurposes.Where(p => p.AccountId == account.Id).Select(p => p.Purpose).ToListAsync(ct);
                if (purposes.Any(p => !AccountPurpose.AllowedTypes(p).Contains(type)))
                {
                    throw BooksProblem.Invalid("This account serves a system purpose that does not allow that type.");
                }
            }
            var group = await FindGroupAsync(ledgerId, command.GroupChanges ? command.GroupId : account.GroupId, ct);
            account.Update(command.Code, command.Name, command.Type, command.Active, command.GroupChanges, group);
            await EnsureCodeFreeAsync(ledgerId, account.Code, account.Id, ct);

            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, account.Id, ct)).Single();
            scope.Audit("account.updated", "account", account.Id, new { before, after });
            return WriteResult.Of(200, after, account.Version);
        }, ct);

    /// <summary>
    /// Deletes an account that was never used (rule 19).
    /// M1 has no postings yet; M2 must refuse accounts referenced by any journal line.
    /// </summary>
    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("account.delete", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == accountId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(account.Version);
            if (await IsUsedAsync(account.Id, ct))
            {
                throw BooksProblem.Conflict("ACCOUNT_IN_USE", "Journals use this account; deactivate it instead (rule 19).");
            }
            if (await db.AccountPurposes.AnyAsync(p => p.AccountId == account.Id, ct))
            {
                throw BooksProblem.Conflict("ACCOUNT_HAS_PURPOSE", "Assign its system purpose to another account first.");
            }
            var before = (await LoadAsync(ledgerId, account.Id, ct)).Single();
            db.Accounts.Remove(account);
            scope.Audit("account.deleted", "account", account.Id, new { before });
            return WriteResult.Of(200, new { id = account.Id, deleted = true });
        }, ct);

    /// <summary>Used = referenced by any journal line, in any revision, including drafts.</summary>
    private Task<bool> IsUsedAsync(Guid accountId, CancellationToken ct) => db.JournalLines.AnyAsync(l => l.AccountId == accountId, ct);

    private async Task<AccountGroup?> FindGroupAsync(Guid ledgerId, Guid? groupId, CancellationToken ct) =>
        groupId is { } id
            ? await db.AccountGroups.SingleOrDefaultAsync(g => g.LedgerId == ledgerId && g.Id == id, ct)
                ?? throw BooksProblem.Invalid("group_id must be a group in the same ledger.")
            : null;

    /// <summary>Rule 16 (case-insensitive), checked under the organization lock; the unique index is the final guard.</summary>
    private async Task EnsureCodeFreeAsync(Guid ledgerId, string code, Guid accountId, CancellationToken ct)
    {
        var key = code.ToLowerInvariant();
        if (await db.Accounts.AnyAsync(a => a.LedgerId == ledgerId && a.Code.ToLower() == key && a.Id != accountId, ct))
        {
            throw BooksProblem.Conflict("ACCOUNT_CODE_TAKEN", $"Code {code} is already used in this ledger.");
        }
    }

    private async Task<IReadOnlyList<AccountDto>> LoadAsync(Guid ledgerId, Guid? accountId, CancellationToken ct)
    {
        var rows = await db.Accounts.AsNoTracking()
            .Where(a => a.LedgerId == ledgerId && (accountId == null || a.Id == accountId))
            .OrderBy(a => a.Code)
            .Select(a => new { a, Used = db.JournalLines.Any(l => l.AccountId == a.Id) })
            .ToListAsync(ct);
        var purposes = (await db.AccountPurposes.AsNoTracking().Where(p => p.LedgerId == ledgerId).ToListAsync(ct))
            .ToLookup(p => p.AccountId, p => Text.Of(p.Purpose));
        return rows.Select(x => new AccountDto(
            x.a.Id, x.a.LedgerId, x.a.GroupId, x.a.Code, x.a.Name, Text.Of(x.a.Type), x.a.IsControl, x.a.IsBank, x.a.Currency,
            x.a.Active, x.a.Postable, x.Used, purposes[x.a.Id].ToList(), x.a.Version.ToString())).ToList();
    }
}
