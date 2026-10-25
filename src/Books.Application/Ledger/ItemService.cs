using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record ItemCategoryDto(Guid Id, string Name, string Version);

public sealed record ItemDto(
    Guid Id, Guid? CategoryId, string Name, string? Description, string? Unit,
    Guid? DefaultAccountId, Guid? DefaultTaxCodeId, bool Active, bool Used, string Version, bool TrackInventory = false,
    Guid? InventoryAccountId = null, Guid? IncomeAccountId = null, Guid? CostOfSalesAccountId = null,
    string? Code = null, string? SalesPrice = null, string? SalesDescription = null, Guid? SalesTaxCodeId = null,
    string? PurchasePrice = null, string? PurchaseDescription = null, Guid? PurchaseTaxCodeId = null, IReadOnlyList<ItemUnitDto>? Units = null,
    IReadOnlyList<ItemComponentDto>? Components = null);

/// <param name="Quantity">Base units of the component in one base unit of the item.</param>
public sealed record ItemComponentDto(Guid ItemId, string Quantity);

/// <param name="Factor">Base units in one of this unit, as a decimal string.</param>
public sealed record ItemUnitDto(string Name, string Factor);

/// <param name="Units">Other units the item is bought or sold in; replaces the item's list.</param>
/// <param name="Code">The item's own code (SKU), unique in the ledger.</param>
/// <param name="SalesPrice">In the ledger's currency; a sales line naming the item without a price or amount uses it.</param>
/// <param name="PurchasePrice">In the ledger's currency; a purchase line naming the item without a price or amount uses it.</param>
/// <param name="InventoryAccountId">Inventory items: the asset account (with the Inventory usage) holding the stock; omit for the ledger's.</param>
/// <param name="IncomeAccountId">Inventory items: where sales of the item post; omit to use each sales line's account.</param>
/// <param name="CostOfSalesAccountId">Inventory items: where the cost of its sales posts; omit for the ledger's cost of sales account.</param>
public sealed record ItemInput(string Name, string? Description, string? Unit, Guid? CategoryId, Guid? DefaultAccountId, Guid? DefaultTaxCodeId, bool Active = true,
    bool TrackInventory = false, Guid? InventoryAccountId = null, Guid? IncomeAccountId = null, Guid? CostOfSalesAccountId = null,
    string? Code = null, decimal? SalesPrice = null, string? SalesDescription = null, Guid? SalesTaxCodeId = null,
    decimal? PurchasePrice = null, string? PurchaseDescription = null, Guid? PurchaseTaxCodeId = null, IReadOnlyList<(string Name, decimal Factor)>? Units = null,
    IReadOnlyList<(Guid ItemId, decimal Quantity)>? Components = null);

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
        writes.RunAsync(principal, new WriteRequest("item_category.create", orgId, meta, LedgerId: ledgerId), async scope =>
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
        writes.RunAsync(principal, new WriteRequest("item.create", orgId, meta, LedgerId: ledgerId), async scope =>
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
        writes.RunAsync(principal, new WriteRequest("item.update", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var item = await db.Items.Include(i => i.Units).Include(i => i.Components).SingleOrDefaultAsync(i => i.LedgerId == ledgerId && i.Id == itemId, ct) ?? throw BooksProblem.NotFound();
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
        async Task<Account?> AccountAsync(Guid? id, string field) => id is { } value
            ? await db.Accounts.SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.Id == value, ct) ?? throw BooksProblem.Invalid($"{field} must be an account in this ledger.")
            : null;
        var inventory = await AccountAsync(input.InventoryAccountId, "inventory_account_id");
        if (inventory is not null && !await db.AccountUsages.AnyAsync(u => u.AccountId == inventory.Id && u.Usage == Usage.Inventory, ct))
        {
            throw BooksProblem.Invalid("inventory_account_id must be an account with the Inventory usage, so the stock report checks it.");
        }
        // Owner decision: an item's stock stays in one asset account. Once the item has postings, its inventory account
        // (its own, or the ledger's it used) and whether it is tracked cannot change; income and cost of sales accounts
        // may change, for documents saved from then on.
        if (!isNew && await db.JournalLines.AnyAsync(l => l.LedgerId == ledgerId && l.ItemId == item.Id, ct))
        {
            if (input.TrackInventory != item.TrackInventory)
            {
                throw BooksProblem.Conflict("ITEM_IN_USE", "This item has postings; whether it tracks inventory cannot change.");
            }
            if (item.TrackInventory && (input.InventoryAccountId ?? await DefaultInventoryAsync(ledgerId, ct)) != (item.InventoryAccountId ?? await DefaultInventoryAsync(ledgerId, ct)))
            {
                throw BooksProblem.Conflict("INVENTORY_ACCOUNT_LOCKED",
                    "This item has postings, so its stock stays in its inventory account; record a journal moving the stock value if it must move.");
            }
        }
        async Task<TaxCode?> TaxAsync(Guid? id, string field, params TaxKind[] kinds) => id is { } value
            ? await db.TaxCodes.SingleOrDefaultAsync(x => x.LedgerId == ledgerId && x.Id == value && kinds.Contains(x.Kind), ct)
                ?? throw BooksProblem.Invalid($"{field} must be a {string.Join(", ", kinds.Select(Text.Of))} tax code in this ledger.")
            : null;
        var salesTax = await TaxAsync(input.SalesTaxCodeId, "sales_tax_code_id", TaxKind.VatOutput, TaxKind.VatZeroRated, TaxKind.NonVat);
        var purchaseTax = await TaxAsync(input.PurchaseTaxCodeId, "purchase_tax_code_id", TaxKind.VatInput, TaxKind.VatInputNonDeductible, TaxKind.VatZeroRated, TaxKind.NonVat);
        var code = string.IsNullOrWhiteSpace(input.Code) ? null : input.Code.Trim().ToLowerInvariant();
        if (code is not null && await db.Items.AnyAsync(x => x.LedgerId == ledgerId && x.Id != item.Id && x.Code != null && x.Code.ToLower() == code, ct))
        {
            throw BooksProblem.Conflict("ITEM_CODE_EXISTS", $"Another item already has the code {input.Code!.Trim()}.");
        }
        try
        {
            item.Set(input.Name, input.Description, input.Unit, category, account, taxCode, input.Active, isNew, input.TrackInventory,
                inventory, await AccountAsync(input.IncomeAccountId, "income_account_id"), await AccountAsync(input.CostOfSalesAccountId, "cost_of_sales_account_id"));
            item.SetTrading(input.Code, input.SalesPrice, input.SalesDescription, salesTax, input.PurchasePrice, input.PurchaseDescription, purchaseTax);
            if (input.Components is { } components)
            {
                var ids = components.Select(c => c.ItemId).ToList();
                if (await db.Items.CountAsync(x => x.LedgerId == ledgerId && x.TrackInventory && ids.Contains(x.Id), ct) != ids.Distinct().Count() || !input.TrackInventory)
                {
                    throw BooksProblem.Invalid("A bill of materials is for an inventory item, and its components must be inventory items of this ledger.");
                }
                db.ItemComponents.RemoveRange(item.Components);
                item.SetComponents(components);
            }
            if (input.Units is { } units)
            {
                db.ItemUnits.RemoveRange(item.Units);
                item.SetUnits(units);
            }
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }

    /// <summary>The ledger's only account with the Inventory usage, or null when there is not exactly one.</summary>
    private async Task<Guid?> DefaultInventoryAsync(Guid ledgerId, CancellationToken ct)
    {
        var accounts = await db.AccountUsages.Where(u => u.LedgerId == ledgerId && u.Usage == Usage.Inventory).Select(u => u.AccountId).Take(2).ToListAsync(ct);
        return accounts.Count == 1 ? accounts[0] : null;
    }

    private static string? P(decimal? price) => price?.ToString("0.00##", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<ItemDto>> LoadAsync(Guid ledgerId, Guid? itemId, CancellationToken ct) =>
        (await db.Items.AsNoTracking().Include(i => i.Units).Include(i => i.Components).Where(i => i.LedgerId == ledgerId && (itemId == null || i.Id == itemId))
            .OrderBy(i => i.Code ?? i.Name).ThenBy(i => i.Name).ThenBy(i => i.Id)
            .Select(i => new { i, Used = db.JournalLines.Any(l => l.ItemId == i.Id) })
            .ToListAsync(ct))
        .Select(x => new ItemDto(x.i.Id, x.i.CategoryId, x.i.Name, x.i.Description, x.i.Unit, x.i.DefaultAccountId, x.i.DefaultTaxCodeId, x.i.Active, x.Used, x.i.Version.ToString(), x.i.TrackInventory,
            x.i.InventoryAccountId, x.i.IncomeAccountId, x.i.CostOfSalesAccountId, x.i.Code, P(x.i.SalesPrice), x.i.SalesDescription, x.i.SalesTaxCodeId,
            P(x.i.PurchasePrice), x.i.PurchaseDescription, x.i.PurchaseTaxCodeId,
            x.i.Units.OrderBy(u => u.Factor).Select(u => new ItemUnitDto(u.Name, u.Factor.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))).ToList(),
            x.i.Components.Select(c => new ItemComponentDto(c.ComponentId, c.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))).ToList()))
        .ToList();
}
