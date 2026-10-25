using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// A warehouse, shop or other place stock is kept (M10.9, Manager.io's inventory locations). Quantities are tracked
/// per location; the cost is one average per item across all locations (owner decision), so moving stock between
/// locations changes no value. A ledger without locations keeps all stock in one place, as before.
/// </summary>
public sealed class Location
{
    private Location() { }

    public Location(Guid id, Ledger ledger, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Code { get; private set; }

    /// <summary>Lines that name no location use the default one; a ledger with locations has exactly one.</summary>
    public bool IsDefault { get; private set; }

    public bool Active { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Set(string name, string? code, bool isDefault, bool active, bool isNew)
    {
        if (isDefault && !active)
        {
            throw new DomainException("The default location must be active.");
        }
        Name = Names.Require(name);
        var trimmed = code?.Trim();
        Code = string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= 20 ? trimmed : throw new DomainException("code must be at most 20 characters.");
        IsDefault = isDefault;
        Active = active;
        if (!isNew)
        {
            Version++;
        }
    }

    public void NotDefault()
    {
        if (IsDefault)
        {
            IsDefault = false;
            Version++;
        }
    }
}
