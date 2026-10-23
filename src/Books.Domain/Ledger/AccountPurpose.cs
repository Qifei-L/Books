using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// System purposes an account can serve in a ledger (rule 17). Each purpose has at most one account.
/// Suspense holds amounts whose classification is not decided yet, so a journal can still balance (rule 26).
/// </summary>
public enum SystemPurpose { RetainedEarnings, OpeningBalanceClearing, RoundingDifferences, Suspense }

public sealed class AccountPurpose
{
    private AccountPurpose() { }

    public AccountPurpose(Ledger ledger, SystemPurpose purpose, Account account)
    {
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        Purpose = purpose;
        Assign(account);
    }

    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public SystemPurpose Purpose { get; private set; }
    public Guid AccountId { get; private set; }

    /// <summary>The account must be active, in this ledger, and of a type the purpose allows (owner decision 2026-10-23).</summary>
    public void Assign(Account account)
    {
        if (account.LedgerId != LedgerId || !account.Active)
        {
            throw new DomainException("A system purpose needs an active account in this ledger.");
        }
        if (!AllowedTypes(Purpose).Contains(account.Type))
        {
            throw new DomainException($"This purpose needs an account of type {string.Join(" or ", AllowedTypes(Purpose))}.");
        }
        AccountId = account.Id;
    }

    public static AccountType[] AllowedTypes(SystemPurpose purpose) => purpose switch
    {
        SystemPurpose.RetainedEarnings or SystemPurpose.OpeningBalanceClearing => [AccountType.Equity],
        SystemPurpose.RoundingDifferences => [AccountType.Income, AccountType.CostAndExpense],
        _ => [AccountType.Assets, AccountType.Liabilities, AccountType.Equity],
    };
}
