using Books.Domain.Access;

namespace Books.Domain.Documents;

/// <summary>
/// What a ledger prints on its invoices (M9): the seller's names and address in Khmer and English, its VAT TIN and
/// contact, and the prefixes shown before invoice and credit note numbers (owner decision: per-ledger prefixes).
/// </summary>
public sealed class InvoiceSettings
{
    private InvoiceSettings() { }

    public InvoiceSettings(Guid ledgerId, Guid orgId)
    {
        LedgerId = ledgerId;
        OrgId = orgId;
    }

    public Guid LedgerId { get; private set; }
    public Guid OrgId { get; private set; }
    public string? NameKhmer { get; private set; }
    public string? NameEnglish { get; private set; }
    public string? TaxId { get; private set; }
    public string? AddressKhmer { get; private set; }
    public string? AddressEnglish { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string InvoicePrefix { get; private set; } = "INV-";
    public string CreditNotePrefix { get; private set; } = "CN-";
    public long Version { get; private set; } = 1;

    public void Set(string? nameKhmer, string? nameEnglish, string? taxId, string? addressKhmer, string? addressEnglish, string? phone, string? email,
        string? invoicePrefix, string? creditNotePrefix, bool isNew)
    {
        NameKhmer = Optional(nameKhmer, 200);
        NameEnglish = Optional(nameEnglish, 200);
        TaxId = Optional(taxId, 50);
        AddressKhmer = Optional(addressKhmer, 500);
        AddressEnglish = Optional(addressEnglish, 500);
        Phone = Optional(phone, 50);
        Email = Optional(email, 200);
        InvoicePrefix = Optional(invoicePrefix, 20) ?? "";
        CreditNotePrefix = Optional(creditNotePrefix, 20) ?? "";
        if (!isNew)
        {
            Version++;
        }
    }

    /// <summary>The number as printed: the prefix and the system number with at least five digits, for example INV-00042.</summary>
    public string Number(long number, bool creditNote) => $"{(creditNote ? CreditNotePrefix : InvoicePrefix)}{number:D5}";

    private static string? Optional(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null
            : trimmed.Length <= max ? trimmed : throw new DomainException($"Invoice settings allow at most {max} characters per field.");
    }
}
