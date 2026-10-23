using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// A customer, a supplier, or both: one record per counterparty in a ledger. Lines on control accounts (AR/AP)
/// always name a Party (rule 18), so aging can be read per Party.
/// </summary>
public sealed class Party
{
    private Party() { }

    public Party(Guid id, Ledger ledger, string name, DateTimeOffset createdAt)
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

    /// <summary>Tax identification number (VATTIN in Cambodia), printed on invoices.</summary>
    public string? TaxId { get; private set; }

    public bool IsCustomer { get; private set; }
    public bool IsSupplier { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? ContactName { get; private set; }

    /// <summary>Default days until payment is due; documents may override it.</summary>
    public int? PaymentTermsDays { get; private set; }

    public bool Active { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Sets details; null leaves a field unchanged and an empty string clears an optional one.</summary>
    public void Update(
        string? name, string? taxId, bool? isCustomer, bool? isSupplier, string? email, string? phone, string? address,
        string? contactName, int? paymentTermsDays, bool? active, bool isNew = false)
    {
        if (paymentTermsDays is < 0 or > 3650)
        {
            throw new DomainException("payment_terms_days must be 0–3650.");
        }
        Name = name is null ? Name : Names.Require(name);
        TaxId = taxId is null ? TaxId : Optional(taxId, 50, "tax_id");
        IsCustomer = isCustomer ?? IsCustomer;
        IsSupplier = isSupplier ?? IsSupplier;
        Email = email is null ? Email : Optional(email, 320, "email");
        Phone = phone is null ? Phone : Optional(phone, 50, "phone");
        Address = address is null ? Address : Optional(address, 500, "address");
        ContactName = contactName is null ? ContactName : Optional(contactName, 200, "contact_name");
        PaymentTermsDays = paymentTermsDays ?? PaymentTermsDays;
        Active = active ?? Active;
        if (!isNew)
        {
            Version++;
        }
    }

    private static string? Optional(string value, int max, string field)
    {
        var trimmed = value.Trim();
        return trimmed.Length > max ? throw new DomainException($"{field} must be at most {max} characters.") : trimmed.Length == 0 ? null : trimmed;
    }
}
