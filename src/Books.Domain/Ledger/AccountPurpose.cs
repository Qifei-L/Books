using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>System purposes an account can serve in a ledger (rule 17). Each purpose has at most one account.</summary>
public enum SystemPurpose { RetainedEarnings, OpeningBalanceClearing, RoundingDifferences }

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

    /// <summary>Retained Earnings must be an Equity account; the account must be active and in this ledger.</summary>
    public void Assign(Account account)
    {
        if (account.LedgerId != LedgerId || !account.Active)
        {
            throw new DomainException("A system purpose needs an active account in this ledger.");
        }
        if (Purpose == SystemPurpose.RetainedEarnings && account.Type != AccountType.Equity)
        {
            throw new DomainException("Retained Earnings must be an Equity account.");
        }
        AccountId = account.Id;
    }
}
