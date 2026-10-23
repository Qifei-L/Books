using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record ItemCategoryDto(Guid Id, string Name, string Version);

public sealed record ItemDto(
    Guid Id, Guid? CategoryId, string Name, string? Description, string? Unit,
    Guid? DefaultAccountId, Guid? DefaultTaxCodeId, bool Active, bool Used, string Version);

public sealed record ItemInput(string Name, string? Description, string? Unit, Guid? CategoryId, Guid? DefaultAccountId, Guid? DefaultTaxCodeId, bool Active = true);

/// <summary>Item categories and items of a ledger. Reading needs read access; changes need edit access.</summary>
public sealed class ItemService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<ItemCategoryDto>> ListCategoriesAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<ItemCategoryDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await db.ItemCategories.AsNoTracking().Where(c => c.LedgerId == ledgerId).OrderBy(c => c.Name).ToListAsync(ct))
                .Select(c => new ItemCategoryDto(c.Id, c.Name, c.Version.ToString())).ToList();
        }, ct);

    public Task<WriteResult> CreateCategoryAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, string name, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("item_category.create", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var category = new ItemCategory(Guid.CreateVersion7(), await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct), name, scope.Now);
            db.ItemCategories.Add(category);
            scope.Audit("item_category.created", "item_category", category.Id, new { name = category.Name });
            return WriteResult.Of(201, new ItemCategoryDto(category.Id, category.Name, category.Version.ToString()), category.Version);
        }, ct);

    public Task<IReadOnlyList<ItemDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, ItemInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("item.create", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var item = new Item(Guid.CreateVersion7(), await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct), input.Name, scope.Now);
            await SetAsync(item, ledgerId, input, isNew: true, ct);
            db.Items.Add(item);
            await db.SaveChangesAsync(ct);
            var dto = (await LoadAsync(ledgerId, item.Id, ct)).Single();
            scope.Audit("item.created", "item", item.Id, dto);
            return WriteResult.Of(201, dto, item.Version);
        }, ct);

    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid itemId, ItemInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("item.update", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var item = await db.Items.SingleOrDefaultAsync(i => i.LedgerId == ledgerId && i.Id == itemId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(item.Version);
            var before = (await LoadAsync(ledgerId, item.Id, ct)).Single();
            await SetAsync(item, ledgerId, input, isNew: false, ct);
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, item.Id, ct)).Single();
            scope.Audit("item.updated", "item", item.Id, new { before, after });
            return WriteResult.Of(200, after, item.Version);
        }, ct);

    private async Task SetAsync(Item item, Guid ledgerId, ItemInput input, bool isNew, CancellationToken ct)
    {
        var category = input.CategoryId is { } c
            ? await db.ItemCategories.SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.Id == c, ct) ?? throw BooksProblem.Invalid("category_id must be a category in this ledger.")
            : null;
        var account = input.DefaultAccountId is { } a
            ? await db.Accounts.SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.Id == a, ct) ?? throw BooksProblem.Invalid("default_account_id must be an account in this ledger.")
            : null;
        var taxCode = input.DefaultTaxCodeId is { } t
            ? await db.TaxCodes.SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.Id == t, ct) ?? throw BooksProblem.Invalid("default_tax_code_id must be a tax code in this ledger.")
            : null;
        item.Set(input.Name, input.Description, input.Unit, category, account, taxCode, input.Active, isNew);
    }

    private async Task<IReadOnlyList<ItemDto>> LoadAsync(Guid ledgerId, Guid? itemId, CancellationToken ct) =>
        (await db.Items.AsNoTracking().Where(i => i.LedgerId == ledgerId && (itemId == null || i.Id == itemId))
            .OrderBy(i => i.Name).ThenBy(i => i.Id)
            .Select(i => new { i, Used = db.JournalLines.Any(l => l.ItemId == i.Id) })
            .ToListAsync(ct))
        .Select(x => new ItemDto(x.i.Id, x.i.CategoryId, x.i.Name, x.i.Description, x.i.Unit, x.i.DefaultAccountId, x.i.DefaultTaxCodeId, x.i.Active, x.Used, x.i.Version.ToString()))
        .ToList();
}
