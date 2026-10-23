using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>
/// Bank details for one Assets account (one bank account per ledger account, owner decision). The account is
/// what journals post to; this record adds what statements and payments need.
/// </summary>
public sealed class BankAccount
{
    private BankAccount() { }

    public BankAccount(Ledger ledger, Account account, string bankName, string? accountNumber, DateTimeOffset createdAt)
    {
        if (account.LedgerId != ledger.Id || account.Type != AccountType.Assets)
        {
            throw new DomainException("A bank account needs an Assets account in this ledger.");
        }
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        AccountId = account.Id;
        Currency = ledger.BaseCurrency;
        CreatedAt = createdAt;
        Set(bankName, accountNumber);
        account.MarkAsBank();
    }

    public Guid AccountId { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public string BankName { get; private set; } = "";
    public string? AccountNumber { get; private set; }

    /// <summary>The ledger's base currency for now; foreign-currency bank accounts come with multi-currency.</summary>
    public string Currency { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Update(string bankName, string? accountNumber)
    {
        Set(bankName, accountNumber);
        Version++;
    }

    private void Set(string bankName, string? accountNumber)
    {
        BankName = Names.Require(bankName, "bank_name");
        var number = accountNumber?.Trim();
        AccountNumber = string.IsNullOrEmpty(number) ? null : number.Length <= 50 ? number : throw new DomainException("account_number must be at most 50 characters.");
    }
}

/// <summary>
/// A daily rate entered by hand, for example the NBC rate used for KHR totals on invoices and monthly tax.
/// Rate = units of <see cref="ToCurrency"/> per one unit of <see cref="FromCurrency"/>.
/// </summary>
public sealed class ExchangeRate
{
    private ExchangeRate() { }

    public ExchangeRate(Guid orgId, DateOnly date, string fromCurrency, string toCurrency, decimal rate, string source, DateTimeOffset createdAt)
    {
        OrgId = orgId;
        Date = date;
        FromCurrency = Currencies.Require(fromCurrency).Code;
        ToCurrency = Currencies.Require(toCurrency).Code;
        if (FromCurrency == ToCurrency)
        {
            throw new DomainException("A rate needs two different currencies.");
        }
        CreatedAt = createdAt;
        Set(rate, source);
    }

    public Guid OrgId { get; private set; }
    public DateOnly Date { get; private set; }
    public string FromCurrency { get; private set; } = "";
    public string ToCurrency { get; private set; } = "";
    public decimal Rate { get; private set; }

    /// <summary>Where the rate came from, for example "NBC" or "Manual".</summary>
    public string Source { get; private set; } = "";

    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Replace(decimal rate, string source)
    {
        Set(rate, source);
        Version++;
    }

    private void Set(decimal rate, string source)
    {
        if (rate <= 0 || decimal.Round(rate, 10) != rate || rate > 9_999_999_999m)
        {
            throw new DomainException("rate must be positive with up to 10 decimals.");
        }
        Rate = rate;
        Source = Names.Require(source, "source");
    }
}
