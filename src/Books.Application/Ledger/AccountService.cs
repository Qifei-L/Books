using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record AccountDto(
    Guid Id, Guid LedgerId, Guid? ParentId, string Code, string Name, string Type, bool IsControl, bool IsBank,
    string? Currency, bool Active, bool IsLeaf, bool Postable, string Version);

public sealed record CreateAccount(string Code, string Name, AccountType Type, Guid? ParentId, bool IsControl, bool IsBank, string? Currency);

/// <summary>Chart of accounts. Reading needs ledger read access; changes need edit access.</summary>
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
            var parent = command.ParentId is { } parentId
                ? await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == parentId, ct)
                    ?? throw BooksProblem.Invalid("parent_id must be an account in the same ledger.")
                : null;
            var account = new Account(Guid.CreateVersion7(), ledger, command.Code, command.Name, command.Type, parent,
                command.IsControl, command.IsBank, command.Currency, scope.Now);
            await EnsureCodeFreeAsync(ledgerId, account.Code, account.Id, ct);

            db.Accounts.Add(account);
            scope.Audit("account.created", "account", account.Id, new
            {
                ledger_id = ledgerId,
                code = account.Code,
                name = account.Name,
                type = Text.Of(account.Type),
                parent_id = account.ParentId,
            });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, (await LoadAsync(ledgerId, account.Id, ct)).Single(), account.Version);
        }, ct);

    /// <summary>Rule 16, checked under the organization lock; the unique index is the final guard.</summary>
    private async Task EnsureCodeFreeAsync(Guid ledgerId, string code, Guid accountId, CancellationToken ct)
    {
        if (await db.Accounts.AnyAsync(a => a.LedgerId == ledgerId && a.Code == code && a.Id != accountId, ct))
        {
            throw BooksProblem.Conflict("ACCOUNT_CODE_TAKEN", $"Code {code} is already used in this ledger.");
        }
    }

    private async Task<IReadOnlyList<AccountDto>> LoadAsync(Guid ledgerId, Guid? accountId, CancellationToken ct)
    {
        var rows = await db.Accounts.AsNoTracking()
            .Where(a => a.LedgerId == ledgerId && (accountId == null || a.Id == accountId))
            .Select(a => new { a, HasChildren = db.Accounts.Any(c => c.LedgerId == a.LedgerId && c.ParentId == a.Id) })
            .OrderBy(x => x.a.Code)
            .ToListAsync(ct);
        return rows.Select(x => new AccountDto(
            x.a.Id, x.a.LedgerId, x.a.ParentId, x.a.Code, x.a.Name, Text.Of(x.a.Type), x.a.IsControl, x.a.IsBank, x.a.Currency,
            x.a.Active, !x.HasChildren, Account.CanPost(x.a.Active, x.HasChildren), x.a.Version.ToString())).ToList();
    }
}
