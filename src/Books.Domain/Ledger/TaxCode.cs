using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// VAT output (sales) and input (purchases) at a rate; zero-rated VAT and non-VAT are separate kinds with no tax
/// (owner decision); withholding tax at a rate.
/// </summary>
public enum TaxKind { VatOutput, VatInput, VatZeroRated, NonVat, Withholding }

/// <summary>
/// A tax a person chooses on a document line (rule 34). Owned by the ledger and maintained by administrators.
/// Effective dates let a rate apply only for a period, for example 4% VAT on fuel from March 2026.
/// </summary>
public sealed class TaxCode
{
    private TaxCode() { }

    public TaxCode(Guid id, Ledger ledger, string name, TaxKind kind, decimal rate, Account? account, DateOnly? effectiveFrom, DateOnly? effectiveTo, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        Kind = kind;
        CreatedAt = createdAt;
        Set(name, rate, account, effectiveFrom, effectiveTo);
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public string Name { get; private set; } = "";
    public TaxKind Kind { get; private set; }

    /// <summary>Percent, for example 10 for 10%.</summary>
    public decimal Rate { get; private set; }

    /// <summary>Where the tax is posted: a liability for output VAT and withholding, an asset for input VAT; none for 0% and non-VAT.</summary>
    public Guid? AccountId { get; private set; }

    public DateOnly? EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool Active { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public bool AppliesOn(DateOnly date) =>
        Active && (EffectiveFrom is null || date >= EffectiveFrom) && (EffectiveTo is null || date <= EffectiveTo);

    public void Update(string name, decimal rate, Account? account, DateOnly? effectiveFrom, DateOnly? effectiveTo, bool active)
    {
        Set(name, rate, account, effectiveFrom, effectiveTo);
        Active = active;
        Version++;
    }

    private void Set(string name, decimal rate, Account? account, DateOnly? effectiveFrom, DateOnly? effectiveTo)
    {
        var carriesTax = Kind is not (TaxKind.VatZeroRated or TaxKind.NonVat);
        if (!carriesTax && (rate != 0 || account is not null))
        {
            throw new DomainException("Zero-rated and non-VAT codes have rate 0 and no tax account.");
        }
        if (carriesTax && (rate <= 0 || rate > 100 || decimal.Round(rate, 4) != rate))
        {
            throw new DomainException("rate must be a percent above 0 and at most 100, with up to 4 decimals.");
        }
        if (carriesTax && (account is null || account.LedgerId != LedgerId || account.Type != RequiredAccountType()))
        {
            throw new DomainException($"This tax code needs a {Text(RequiredAccountType())} account in the same ledger.");
        }
        if (effectiveFrom is { } from && effectiveTo is { } to && to < from)
        {
            throw new DomainException("effective_to must not be before effective_from.");
        }
        Name = Names.Require(name);
        Rate = rate;
        AccountId = account?.Id;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    private AccountType RequiredAccountType() => Kind == TaxKind.VatInput ? AccountType.Assets : AccountType.Liabilities;

    private static string Text(AccountType type) => type == AccountType.Assets ? "an Assets" : "a Liabilities";
}

/// <summary>The tax on one document line: calculated from the code, and possibly overridden by a person.</summary>
/// <param name="Calculated">Net × rate, rounded per line (rule 34).</param>
/// <param name="Amount">The tax actually used; equals Calculated unless overridden.</param>
/// <param name="Reason">Optional reason given for an override.</param>
public sealed record LineTax(decimal Calculated, decimal Amount, string? Reason)
{
    /// <summary>True when a person entered a different amount; the tax module reports these lines.</summary>
    public bool Adjusted => Amount != Calculated;
}

public static class TaxCalculator
{
    /// <summary>
    /// Tax per line, rounded to the currency's minor unit half away from zero (0.005 → 0.01, −0.005 → −0.01),
    /// then summed by the caller (rule 34, owner decision). An override may take any value; both amounts are kept.
    /// </summary>
    public static LineTax ForLine(decimal net, decimal ratePercent, int minorUnits, decimal? overrideAmount = null, string? reason = null)
    {
        var calculated = decimal.Round(net * ratePercent / 100m, minorUnits, MidpointRounding.AwayFromZero);
        if (overrideAmount is not { } entered)
        {
            return new LineTax(calculated, calculated, null);
        }
        if (decimal.Round(entered, minorUnits) != entered)
        {
            throw new DomainException($"The tax amount allows {minorUnits} decimal places.");
        }
        return new LineTax(calculated, entered, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
    }
}
