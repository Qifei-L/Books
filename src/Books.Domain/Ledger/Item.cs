using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>A heading that groups items, for example "Fuel" or "Consulting". Becomes the inventory category later.</summary>
public sealed class ItemCategory
{
    private ItemCategory() { }

    public ItemCategory(Guid id, Ledger ledger, string name, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        Name = Names.Require(name);
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public string Name { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Rename(string name)
    {
        Name = Names.Require(name);
        Version++;
    }
}

/// <summary>
/// Something bought or sold. In v1 it only suggests an account and a tax code for a line (rule 23); the line
/// keeps the account the person chose. Items are designed to become inventory items later (see 03-architecture).
/// </summary>
public sealed class Item
{
    private Item() { }

    public Item(Guid id, Ledger ledger, string name, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        Name = Names.Require(name);
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }

    /// <summary>Unit shown on documents, for example "pcs", "litre" or "hour".</summary>
    public string? Unit { get; private set; }

    public Guid? DefaultAccountId { get; private set; }
    public Guid? DefaultTaxCodeId { get; private set; }

    /// <summary>The item's own code (SKU), unique in the ledger; optional.</summary>
    public string? Code { get; private set; }

    /// <summary>Sales side (Manager.io, QuickBooks and Xero style): the price, text and tax code a sales line starts with.</summary>
    public decimal? SalesPrice { get; private set; }

    public string? SalesDescription { get; private set; }
    public Guid? SalesTaxCodeId { get; private set; }

    /// <summary>Purchase side: the price, text and tax code a purchase line starts with.</summary>
    public decimal? PurchasePrice { get; private set; }

    public string? PurchaseDescription { get; private set; }
    public Guid? PurchaseTaxCodeId { get; private set; }
    public bool Active { get; private set; } = true;

    /// <summary>
    /// An inventory item (Manager.io style): purchases go to the inventory account, sales take their cost at the
    /// average cost into cost of sales, and quantities are tracked.
    /// </summary>
    public bool TrackInventory { get; private set; }

    /// <summary>
    /// Inventory items, Manager.io style: the asset account its stock is held in (one with the Inventory usage), the
    /// income account its sales post to, and the expense account its cost of sales posts to. Null uses the ledger's.
    /// </summary>
    public Guid? InventoryAccountId { get; private set; }

    public Guid? IncomeAccountId { get; private set; }
    public Guid? CostOfSalesAccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Sets the code and the sales and purchase defaults; tax code kinds are checked by the caller.</summary>
    public void SetTrading(string? code, decimal? salesPrice, string? salesDescription, TaxCode? salesTaxCode,
        decimal? purchasePrice, string? purchaseDescription, TaxCode? purchaseTaxCode)
    {
        if (salesPrice < 0 || purchasePrice < 0)
        {
            throw new DomainException("Prices cannot be negative.");
        }
        if ((salesTaxCode is not null && salesTaxCode.LedgerId != LedgerId) || (purchaseTaxCode is not null && purchaseTaxCode.LedgerId != LedgerId))
        {
            throw new DomainException("Tax codes must be in the same ledger.");
        }
        var trimmed = code?.Trim();
        Code = string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= 50 ? trimmed : throw new DomainException("code must be at most 50 characters.");
        SalesPrice = salesPrice;
        SalesDescription = Text(salesDescription);
        SalesTaxCodeId = salesTaxCode?.Id;
        PurchasePrice = purchasePrice;
        PurchaseDescription = Text(purchaseDescription);
        PurchaseTaxCodeId = purchaseTaxCode?.Id;
    }

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= 1000 ? value.Trim() : throw new DomainException("Descriptions are at most 1000 characters.");

    /// <summary>Replaces all details. References are checked against the same ledger by the caller and the database.</summary>
    public void Set(string name, string? description, string? unit, ItemCategory? category, Account? defaultAccount, TaxCode? defaultTaxCode, bool active, bool isNew = false,
        bool trackInventory = false, Account? inventoryAccount = null, Account? incomeAccount = null, Account? costOfSalesAccount = null)
    {
        if (!trackInventory && (inventoryAccount ?? incomeAccount ?? costOfSalesAccount) is not null)
        {
            throw new DomainException("Inventory, income and cost of sales accounts are for inventory items; set track_inventory.");
        }
        if ((inventoryAccount is not null && (inventoryAccount.LedgerId != LedgerId || inventoryAccount.Type != AccountType.Assets))
            || (incomeAccount is not null && (incomeAccount.LedgerId != LedgerId || incomeAccount.Type != AccountType.Income))
            || (costOfSalesAccount is not null && (costOfSalesAccount.LedgerId != LedgerId || costOfSalesAccount.Type != AccountType.CostAndExpense)))
        {
            throw new DomainException("The inventory account must be an asset, the income account income and the cost of sales account an expense of this ledger.");
        }
        TrackInventory = trackInventory;
        InventoryAccountId = inventoryAccount?.Id;
        IncomeAccountId = incomeAccount?.Id;
        CostOfSalesAccountId = costOfSalesAccount?.Id;
        if ((category is not null && category.LedgerId != LedgerId)
            || (defaultAccount is not null && defaultAccount.LedgerId != LedgerId)
            || (defaultTaxCode is not null && defaultTaxCode.LedgerId != LedgerId))
        {
            throw new DomainException("Category, default account and default tax code must be in the same ledger.");
        }
        Name = Names.Require(name);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim().Length <= 20 ? unit.Trim() : throw new DomainException("unit must be at most 20 characters.");
        CategoryId = category?.Id;
        DefaultAccountId = defaultAccount?.Id;
        DefaultTaxCodeId = defaultTaxCode?.Id;
        Active = active;
        if (!isNew)
        {
            Version++;
        }
    }
}
