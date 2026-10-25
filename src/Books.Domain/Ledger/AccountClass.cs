using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// A ledger classification used to report income, expense and balances by segment (for example a product line,
/// department or business unit). It is a reporting dimension, not a subledger: lines may carry one class.
/// </summary>
public sealed class AccountClass
{
    private AccountClass() { }

    public AccountClass(Guid id, Ledger ledger, string name, string? code, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        CreatedAt = createdAt;
        Set(name, code, true, true);
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Code { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Set(string name, string? code, bool active, bool isNew)
    {
        Name = Names.Require(name);
        var trimmed = code?.Trim();
        Code = string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= 20 ? trimmed : throw new DomainException("code must be at most 20 characters.");
        Active = active;
        if (!isNew)
        {
            Version++;
        }
    }
}
