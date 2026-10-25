using Books.Domain.Access;

namespace Books.Domain.Access;

/// <summary>
/// Client-facing identity used on report packs and tax workpapers. One row per organization; a ledger may later
/// override the signature block, but the legal identity stays organization-wide.
/// </summary>
public sealed class OrganizationBranding
{
    private OrganizationBranding() { }

    public OrganizationBranding(Guid orgId, DateTimeOffset createdAt)
    {
        OrgId = orgId;
        CreatedAt = createdAt;
    }

    public Guid OrgId { get; private set; }
    public string? LegalName { get; private set; }
    public string? LegalNameKm { get; private set; }
    public string? VatTin { get; private set; }
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? BrandColor { get; private set; }
    public string? Footer { get; private set; }
    public string? PreparedBy { get; private set; }
    public string? ReviewedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Set(string? legalName, string? legalNameKm, string? vatTin, string? address, string? phone, string? email,
        string? brandColor, string? footer, string? preparedBy, string? reviewedBy, DateTimeOffset now)
    {
        LegalName = Optional(legalName, 200, "legal_name");
        LegalNameKm = Optional(legalNameKm, 200, "legal_name_km");
        VatTin = Optional(vatTin, 50, "vat_tin");
        Address = Optional(address, 500, "address");
        Phone = Optional(phone, 50, "phone");
        Email = Optional(email, 320, "email");
        BrandColor = Optional(brandColor, 20, "brand_color");
        Footer = Optional(footer, 500, "footer");
        PreparedBy = Optional(preparedBy, 200, "prepared_by");
        ReviewedBy = Optional(reviewedBy, 200, "reviewed_by");
        UpdatedAt = now;
        Version++;
    }

    private static string? Optional(string? value, int max, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > max ? throw new DomainException($"{field} must be at most {max} characters.") : trimmed;
    }
}
