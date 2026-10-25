using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record ClassInput(string Name, string? Code, bool? Active);

public sealed record ClassDto(Guid Id, string Name, string? Code, bool Active, string Version);

/// <summary>Classes of a ledger: reporting dimensions carried by journal and document lines.</summary>
public sealed class ClassService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<ClassDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<ClassDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await db.Classes.AsNoTracking().Where(c => c.LedgerId == ledgerId).OrderBy(c => c.Name).ThenBy(c => c.Id).ToListAsync(ct))
                .Select(ToDto).ToList();
        }, ct);

    public Task<WriteResult> SaveAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid? classId, ClassInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("class.save", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                throw BooksProblem.Invalid("name is required.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var all = await db.Classes.Where(c => c.LedgerId == ledgerId).ToListAsync(ct);
            AccountClass item;
            if (classId is { } id)
            {
                item = all.SingleOrDefault(c => c.Id == id) ?? throw BooksProblem.NotFound();
                scope.CheckVersion(item.Version);
            }
            else
            {
                try
                {
                    item = new AccountClass(Guid.CreateVersion7(), ledger, input.Name, input.Code, scope.Now);
                }
                catch (DomainException e)
                {
                    throw BooksProblem.Invalid(e.Message);
                }
                db.Classes.Add(item);
                all.Add(item);
            }
            if (all.Any(c => c.Id != item.Id && c.Name.Equals(input.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw BooksProblem.Conflict("CLASS_EXISTS", $"There is already a class named {input.Name.Trim()}.");
            }
            try
            {
                item.Set(input.Name, input.Code, input.Active ?? true, isNew: classId is null);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            scope.Audit(classId is null ? "class.created" : "class.updated", "class", item.Id, ToDto(item));
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(classId is null ? 201 : 200, ToDto(item), item.Version);
        }, ct);

    private static ClassDto ToDto(AccountClass c) => new(c.Id, c.Name, c.Code, c.Active, c.Version.ToString());
}

/// <summary>Which class a document line uses.</summary>
internal static class Classes
{
    /// <summary>The class given (an active one of the ledger), or null when none is given.</summary>
    public static async Task<Guid?> ResolveAsync(IBooksDb db, Guid ledgerId, Guid? classId, string field, CancellationToken ct)
    {
        if (classId is not { } id)
        {
            return null;
        }
        return await db.Classes.AnyAsync(c => c.LedgerId == ledgerId && c.Id == id && c.Active, ct)
            ? id
            : throw BooksProblem.Invalid($"{field} must be an active class of this ledger.");
    }
}
