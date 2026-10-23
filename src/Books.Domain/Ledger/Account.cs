using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>The five account types (rule 14). P&amp;L = Income − Cost &amp; Expense.</summary>
public enum AccountType { Assets, Liabilities, Equity, Income, CostAndExpense }

/// <summary>
/// An account in one ledger's chart. Accounts form a tree within one ledger and type, and only leaf accounts
/// take postings (rule 15). Code is user-entered, editable and unique within the ledger; journals reference the
/// stable Id (rule 16).
/// </summary>
public sealed class Account
{
    public const int MaxCodeLength = 50;

    private Account() { }

    public Account(Guid id, Ledger ledger, string code, string name, AccountType type, Account? parent,
        bool isControl, bool isBank, string? currency, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        Code = RequireCode(code);
        Name = Names.Require(name);
        Type = type;
        IsControl = isControl;
        IsBank = isBank;
        Currency = currency is null ? null : Currencies.Require(currency).Code;
        CreatedAt = createdAt;
        MoveUnder(parent);
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public AccountType Type { get; private set; }

    /// <summary>AR/AP style account posted only by documents, each line with a Party (rule 18, from M4).</summary>
    public bool IsControl { get; private set; }

    public bool IsBank { get; private set; }

    /// <summary>Optional account currency; null means the ledger's base currency.</summary>
    public string? Currency { get; private set; }

    public bool Active { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Only active leaf accounts may be posted to.</summary>
    public static bool CanPost(bool active, bool hasChildren) => active && !hasChildren;

    public static string RequireCode(string? code)
    {
        var trimmed = code?.Trim() ?? "";
        return trimmed.Length is 0 or > MaxCodeLength
            ? throw new DomainException($"code must be 1–{MaxCodeLength} characters.")
            : trimmed;
    }

    private void MoveUnder(Account? parent)
    {
        if (parent is not null && (parent.LedgerId != LedgerId || parent.Type != Type))
        {
            throw new DomainException("A parent account must be in the same ledger and of the same type.");
        }
        ParentId = parent?.Id;
    }
}
