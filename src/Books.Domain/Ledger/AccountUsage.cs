using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// Business purposes an account can serve (Account Usage). The list is defined by the system, per subsystem
/// (owner decision). Many-to-many: an account may carry several usages and a usage may sit on several accounts.
/// Distinct from account type, system purposes and permissions.
/// </summary>
public enum Usage
{
    Payable, Receivable, Bank, OutputVat, InputVat, NonDeductibleVat, WithholdingPayable,

    // Payroll results (pay runs) post to these.
    SalaryExpense, NssfExpense, SalariesPayable, SalaryTaxPayable, NssfPayable,

    // Other monthly taxes charged on sales (specific, accommodation, public lighting).
    OtherTaxPayable,

    // Inventory items: stock on hand and what it cost when sold.
    Inventory, CostOfSales,

    // Fixed assets: cost, accumulated depreciation (a contra asset) and the monthly expense.
    FixedAsset, AccumulatedDepreciation, DepreciationExpense,
}

public static class Usages
{
    /// <summary>The account types a usage allows.</summary>
    public static AccountType[] Types(Usage usage) => usage switch
    {
        Usage.Payable or Usage.OutputVat or Usage.WithholdingPayable or Usage.SalariesPayable or Usage.SalaryTaxPayable or Usage.NssfPayable
            or Usage.OtherTaxPayable => [AccountType.Liabilities],
        Usage.Receivable or Usage.Bank or Usage.InputVat or Usage.Inventory or Usage.FixedAsset or Usage.AccumulatedDepreciation => [AccountType.Assets],
        Usage.NonDeductibleVat or Usage.SalaryExpense or Usage.NssfExpense or Usage.CostOfSales or Usage.DepreciationExpense => [AccountType.CostAndExpense],
        _ => throw new ArgumentOutOfRangeException(nameof(usage)),
    };

    /// <summary>Payable and Receivable accounts are control accounts, so aging always equals their balance (rules 18, 43).</summary>
    public static bool RequiresControl(Usage usage) => usage is Usage.Payable or Usage.Receivable;

    /// <summary>The usage a tax code's account must carry, or null for kinds without a tax account.</summary>
    public static Usage? ForTaxKind(TaxKind kind) => kind switch
    {
        TaxKind.VatOutput => Usage.OutputVat,
        TaxKind.VatInput => Usage.InputVat,
        TaxKind.Withholding => Usage.WithholdingPayable,
        TaxKind.OtherTax => Usage.OtherTaxPayable,
        _ => null,
    };
}

public sealed class AccountUsage
{
    private AccountUsage() { }

    /// <summary>Checks the account's type and control flag for the usage; the caller handles marking control.</summary>
    public AccountUsage(Account account, Usage usage)
    {
        if (!Usages.Types(usage).Contains(account.Type))
        {
            throw new DomainException($"The {usage} usage needs an account of type {string.Join(" or ", Usages.Types(usage))}.");
        }
        if (Usages.RequiresControl(usage) && !account.IsControl)
        {
            throw new DomainException($"The {usage} usage needs a control account.");
        }
        OrgId = account.OrgId;
        LedgerId = account.LedgerId;
        AccountId = account.Id;
        Usage = usage;
    }

    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid AccountId { get; private set; }
    public Usage Usage { get; private set; }
}
