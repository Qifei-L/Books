using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>How a ledger's reports and workpapers look when printed or exported.</summary>
public sealed class LedgerReportSettings
{
    private LedgerReportSettings() { }

    public LedgerReportSettings(Guid orgId, Guid ledgerId, DateTimeOffset createdAt)
    {
        OrgId = orgId;
        LedgerId = ledgerId;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid LedgerId { get; private set; }
    public Guid OrgId { get; private set; }
    public string Language { get; private set; } = "en";
    public string Comparative { get; private set; } = "prior_period";
    public bool ShowZeroBalances { get; private set; } = true;
    public bool ShowAccountCodes { get; private set; } = true;
    public string PaperSize { get; private set; } = "A4";
    public string Orientation { get; private set; } = "portrait";
    public string Watermark { get; private set; } = "none";
    public string PackSections { get; private set; } = "pl,bs,tb,gl,aging";
    public int ShareDays { get; private set; } = 7;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Set(string language, string comparative, bool showZeroBalances, bool showAccountCodes, string paperSize,
        string orientation, string watermark, string packSections, int shareDays, DateTimeOffset now)
    {
        Language = OneOf(language, ["en", "km", "bilingual"], "language");
        Comparative = OneOf(comparative, ["none", "prior_period", "year_start", "prior_year"], "comparative");
        PaperSize = OneOf(paperSize, ["A4", "Letter"], "paper_size");
        Orientation = OneOf(orientation, ["portrait", "landscape"], "orientation");
        Watermark = OneOf(watermark, ["none", "draft", "final"], "watermark");
        if (shareDays is < 0 or > 365) throw new DomainException("share_days must be 0–365.");
        ShowZeroBalances = showZeroBalances;
        ShowAccountCodes = showAccountCodes;
        PackSections = string.Join(",", packSections.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s is "pl" or "bs" or "tb" or "gl" or "aging").Distinct());
        ShareDays = shareDays;
        UpdatedAt = now;
        Version++;
    }

    private static string OneOf(string value, string[] allowed, string field)
    {
        var trimmed = value.Trim();
        return allowed.Contains(trimmed) ? trimmed : throw new DomainException($"{field} must be one of: {string.Join(", ", allowed)}.");
    }
}
