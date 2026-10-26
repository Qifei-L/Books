using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// A party's details in one language. The record itself keeps the primary (English) name; a localization adds the
/// Khmer name and address used on printed invoices and statements.
/// </summary>
public sealed class PartyLocalization
{
    public const string Khmer = "km";

    private PartyLocalization() { }

    public PartyLocalization(Guid partyId, string locale, string name, string? address, DateTimeOffset updatedAt)
    {
        if (locale != Khmer)
        {
            throw new DomainException("locale must be km.");
        }
        PartyId = partyId;
        Locale = locale;
        Name = Names.Require(name, "name_km");
        Address = Optional(address);
        UpdatedAt = updatedAt;
    }

    public Guid PartyId { get; private set; }
    public string Locale { get; private set; } = Khmer;
    public string Name { get; private set; } = "";
    public string? Address { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Set(string name, string? address, DateTimeOffset now)
    {
        Name = Names.Require(name, "name_km");
        Address = Optional(address);
        UpdatedAt = now;
    }

    private static string? Optional(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        return trimmed.Length > 500 ? throw new DomainException("address_km must be at most 500 characters.") : trimmed;
    }
}
