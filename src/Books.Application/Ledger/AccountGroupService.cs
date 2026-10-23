using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record AccountGroupDto(Guid Id, Guid LedgerId, Guid? ParentId, string Name, string Type, string Version);

/// <param name="ParentChanges">True when parent_id was sent; ParentId null then means the top level.</param>
public sealed record UpdateAccountGroup(string? Name, bool ParentChanges, Guid? ParentId);

/// <summary>Groups are headings in the chart. They never take postings and hold accounts or other groups of one type.</summary>
public sealed class AccountGroupService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<AccountGroupDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, string name, AccountType type, Guid? parentId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("account_group.create", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var group = new AccountGroup(Guid.CreateVersion7(), ledger, name, type, await FindAsync(ledgerId, parentId, ct), scope.Now);
            db.AccountGroups.Add(group);
            scope.Audit("account_group.created", "account_group", group.Id, new { ledger_id = ledgerId, name = group.Name, type = Text.Of(type), parent_id = group.ParentId });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, (await LoadAsync(ledgerId, group.Id, ct)).Single(), group.Version);
        }, ct);

    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid groupId, UpdateAccountGroup command, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("account_group.update", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var group = await db.AccountGroups.SingleOrDefaultAsync(g => g.LedgerId == ledgerId && g.Id == groupId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(group.Version);
            var before = (await LoadAsync(ledgerId, group.Id, ct)).Single();
            var parent = command.ParentChanges ? await FindAsync(ledgerId, command.ParentId, ct) : null;
            if (parent is not null)
            {
                await EnsureNotInsideAsync(group.Id, parent, ct);
            }
            group.Update(command.Name, command.ParentChanges, parent);
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, group.Id, ct)).Single();
            scope.Audit("account_group.updated", "account_group", group.Id, new { before, after });
            return WriteResult.Of(200, after, group.Version);
        }, ct);

    /// <summary>Only an empty group can be deleted: no accounts and no groups inside it.</summary>
    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid groupId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("account_group.delete", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var group = await db.AccountGroups.SingleOrDefaultAsync(g => g.LedgerId == ledgerId && g.Id == groupId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(group.Version);
            if (await db.Accounts.AnyAsync(a => a.GroupId == group.Id, ct) || await db.AccountGroups.AnyAsync(g => g.ParentId == group.Id, ct))
            {
                throw BooksProblem.Conflict("GROUP_NOT_EMPTY", "Move or delete the accounts and groups inside it first.");
            }
            var before = (await LoadAsync(ledgerId, group.Id, ct)).Single();
            db.AccountGroups.Remove(group);
            scope.Audit("account_group.deleted", "account_group", group.Id, new { before });
            return WriteResult.Of(200, new { id = group.Id, deleted = true });
        }, ct);

    private async Task<AccountGroup?> FindAsync(Guid ledgerId, Guid? groupId, CancellationToken ct) =>
        groupId is { } id
            ? await db.AccountGroups.SingleOrDefaultAsync(g => g.LedgerId == ledgerId && g.Id == id, ct)
                ?? throw BooksProblem.Invalid("parent_id must be a group in the same ledger.")
            : null;

    private async Task EnsureNotInsideAsync(Guid groupId, AccountGroup newParent, CancellationToken ct)
    {
        var parents = await db.AccountGroups.AsNoTracking().Where(g => g.LedgerId == newParent.LedgerId)
            .ToDictionaryAsync(g => g.Id, g => g.ParentId, ct);
        for (Guid? current = newParent.Id; current is { } id; current = parents.GetValueOrDefault(id))
        {
            if (id == groupId)
            {
                throw BooksProblem.Invalid("A group cannot be moved inside itself or one of its groups.");
            }
        }
    }

    private async Task<IReadOnlyList<AccountGroupDto>> LoadAsync(Guid ledgerId, Guid? groupId, CancellationToken ct) =>
        (await db.AccountGroups.AsNoTracking()
            .Where(g => g.LedgerId == ledgerId && (groupId == null || g.Id == groupId))
            .OrderBy(g => g.Name).ThenBy(g => g.Id)
            .ToListAsync(ct))
        .Select(g => new AccountGroupDto(g.Id, g.LedgerId, g.ParentId, g.Name, Text.Of(g.Type), g.Version.ToString()))
        .ToList();
}
