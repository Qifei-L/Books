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
    public bool Active { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Replaces all details. References are checked against the same ledger by the caller and the database.</summary>
    public void Set(string name, string? description, string? unit, ItemCategory? category, Account? defaultAccount, TaxCode? defaultTaxCode, bool active, bool isNew = false)
    {
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
