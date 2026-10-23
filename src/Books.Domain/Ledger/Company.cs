using Books.Domain.Access;

// Plural namespace: a namespace named Ledger would hide the Ledger type.
namespace Books.Domain.Ledgers;

/// <summary>A company in an organization. It has exactly one ledger; the database rejects a commit with only one of the pair.</summary>
public sealed class Company
{
    private Company() { }

    public Company(Guid id, Guid orgId, string name, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        Name = Names.Require(name);
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public string Name { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Rename(string name)
    {
        Name = Names.Require(name);
        Version++;
    }
}

/// <summary>The books of one company.</summary>
public sealed class Ledger
{
    private Ledger() { }

    public Ledger(Guid id, Company company, string baseCurrency, int fiscalYearStartMonth, DateTimeOffset createdAt)
    {
        if (fiscalYearStartMonth is < 1 or > 12)
        {
            throw new DomainException("fy_start_month must be 1–12.");
        }
        Id = id;
        OrgId = company.OrgId;
        CompanyId = company.Id;
        BaseCurrency = Currencies.Require(baseCurrency).Code;
        FyStartMonth = fiscalYearStartMonth;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid CompanyId { get; private set; }

    /// <summary>ISO 4217 code chosen at setup. Fixed after the first posting (rule 10, enforced from M2).</summary>
    public string BaseCurrency { get; private set; } = "";

    /// <summary>First month of the fiscal year, 1–12; periods are calendar months.</summary>
    public int FyStartMonth { get; private set; }

    /// <summary>Inclusive Close date; null until the first Close (M2).</summary>
    public DateOnly? ClosedThrough { get; private set; }

    /// <summary>Last Journal No. issued in this ledger; numbers are system-generated and unique per ledger (rule 24).</summary>
    public long LastJournalNo { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>When the first journal was posted; base currency and fiscal year are fixed from then on (rules 10, 12).</summary>
    public DateTimeOffset? FirstPostedAt { get; private set; }

    /// <summary>Issues the next Journal No. Call only while holding the write lock.</summary>
    public long IssueJournalNo() => ++LastJournalNo;

    public void RecordPosting(DateTimeOffset at) => FirstPostedAt ??= at;

    /// <summary>Marks a setup change (such as a system purpose) so concurrent setup edits conflict.</summary>
    public void Touch() => Version++;

    /// <summary>Changes setup before the first posting only; null arguments leave a field unchanged.</summary>
    public void ChangeSettings(string? baseCurrency, int? fiscalYearStartMonth)
    {
        var currency = baseCurrency is null ? BaseCurrency : Currencies.Require(baseCurrency).Code;
        var month = fiscalYearStartMonth ?? FyStartMonth;
        if (FirstPostedAt is not null && (currency != BaseCurrency || month != FyStartMonth))
        {
            throw new DomainException("Base currency and fiscal year start cannot change after the first posting.");
        }
        if (month is < 1 or > 12)
        {
            throw new DomainException("fy_start_month must be 1–12.");
        }
        BaseCurrency = currency;
        FyStartMonth = month;
        Version++;
    }

    /// <summary>Sets the inclusive Close date. Moving it back (reopening) or clearing it is allowed (owner decision).</summary>
    public void CloseThrough(DateOnly? date)
    {
        ClosedThrough = date;
        Version++;
    }
}

public sealed record Currency(string Code, int MinorUnits);

/// <summary>Currencies a ledger may use, with ISO 4217 minor units (rule 11).</summary>
public static class Currencies
{
    public const string Default = "USD";

    private static readonly Dictionary<string, Currency> Supported = new[]
    {
        new Currency("USD", 2),
        new Currency("KHR", 0),
    }.ToDictionary(c => c.Code);

    public static Currency Require(string? code) =>
        code is not null && Supported.TryGetValue(code.Trim().ToUpperInvariant(), out var currency)
            ? currency
            : throw new DomainException($"Currency must be one of: {string.Join(", ", Supported.Keys)}.");
}
