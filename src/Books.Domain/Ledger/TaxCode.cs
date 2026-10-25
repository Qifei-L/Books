using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// VAT output (sales) and input (purchases): input VAT is either creditable or non-creditable by its kind, so the
/// tax code itself decides the account. Zero-rated VAT and non-VAT are separate kinds with no tax; withholding tax
/// at a rate (owner decision 2026-10-25).
/// </summary>
public enum TaxKind { VatOutput, VatInput, VatInputNonDeductible, VatZeroRated, NonVat, Withholding, OtherTax }

/// <summary>How a manual journal's tax line is reported on the monthly return.</summary>
public enum TaxDirection { Sale, SaleAdjustment, Purchase, PurchaseAdjustment }

/// <summary>
/// A tax a person chooses on a document line (rule 34). Owned by the ledger and maintained by administrators.
/// Effective dates let a rate apply only for a period, for example 4% VAT on fuel from March 2026.
/// </summary>
public sealed class TaxCode
{
    private TaxCode() { }

    public TaxCode(Guid id, Ledger ledger, string name, TaxKind kind, decimal rate, Account? account,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, DateTimeOffset createdAt)
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

    /// <summary>Where the tax is posted: a liability for output VAT and withholding, an asset for creditable input VAT,
    /// a Cost &amp; Expense account for non-creditable input VAT; none for 0% and non-VAT.</summary>
    public Guid? AccountId { get; private set; }

    public DateOnly? EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }

    /// <summary>Withholding codes: the tax pack's category the code is reported under on the monthly return.</summary>
    public string? ReturnCategory { get; private set; }

    public bool Active { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <param name="allowed">The ledger's tax pack categories; empty when the ledger has no tax pack.</param>
    public void SetReturnCategory(string? category, IReadOnlyCollection<string> allowed)
    {
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (category is not null && Kind is not (TaxKind.Withholding or TaxKind.VatInput or TaxKind.VatOutput or TaxKind.OtherTax))
        {
            throw new DomainException("Only withholding, VAT and other tax codes have a return category.");
        }
        if (category is not null && !allowed.Contains(category))
        {
            throw new DomainException(allowed.Count == 0
                ? "This ledger has no tax pack, so return_category must be empty."
                : $"return_category must be one of: {string.Join(", ", allowed)}.");
        }
        ReturnCategory = category;
    }

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
        if (carriesTax && (account is null || account.LedgerId != LedgerId || !Usages.Types(Usages.ForTaxKind(Kind)!.Value).Contains(account.Type)))
        {
            throw new DomainException($"This tax code needs a {string.Join(" or ", Usages.Types(Usages.ForTaxKind(Kind)!.Value))} account in the same ledger.");
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

    /// <summary>
    /// Tax included in a gross amount: gross × rate / (100 + rate), rounded per line. Used by manual journals, whose
    /// line amount is the gross the user enters; documents calculate tax on the net instead.
    /// </summary>
    public static LineTax ForGross(decimal gross, decimal ratePercent, int minorUnits)
    {
        var calculated = decimal.Round(gross * ratePercent / (100m + ratePercent), minorUnits, MidpointRounding.AwayFromZero);
        return new LineTax(calculated, calculated, null);
    }
}
