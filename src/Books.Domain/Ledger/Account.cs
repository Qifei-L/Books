using Books.Domain.Access;

namespace Books.Domain.Ledgers;

/// <summary>The five account types (rule 14). P&amp;L = Income − Cost &amp; Expense.</summary>
public enum AccountType { Assets, Liabilities, Equity, Income, CostAndExpense }

/// <summary>
/// A heading in one ledger's chart, like a Manager.io group. Groups never take postings; they may sit inside
/// another group of the same type (rule 15).
/// </summary>
public sealed class AccountGroup
{
    private AccountGroup() { }

    public AccountGroup(Guid id, Ledger ledger, string name, AccountType type, AccountGroup? parent, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = ledger.OrgId;
        LedgerId = ledger.Id;
        Name = Names.Require(name);
        Type = type;
        CreatedAt = createdAt;
        MoveUnder(parent);
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = "";
    public AccountType Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Partial update. The caller has already checked that a new parent is not inside this group.</summary>
    public void Update(string? name, bool parentChanges, AccountGroup? parent)
    {
        if (parentChanges)
        {
            if (parent?.Id == Id)
            {
                throw new DomainException("A group cannot be inside itself.");
            }
            MoveUnder(parent);
        }
        Name = name is null ? Name : Names.Require(name);
        Version++;
    }

    private void MoveUnder(AccountGroup? parent)
    {
        if (parent is not null && (parent.LedgerId != LedgerId || parent.Type != Type))
        {
            throw new DomainException("A parent group must be in the same ledger and of the same type.");
        }
        ParentId = parent?.Id;
    }
}

/// <summary>
/// An account in one ledger's chart. Accounts sit directly under an optional group of the same type and never
/// contain other accounts, so every active account can take postings (rule 15). Code is user-entered, editable
/// and unique within the ledger ignoring case; journals reference the stable Id (rule 16).
/// </summary>
public sealed class Account
{
    public const int MaxCodeLength = 50;

    private Account() { }

    public Account(Guid id, Ledger ledger, string code, string name, AccountType type, AccountGroup? group,
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
        PlaceIn(group);
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid? GroupId { get; private set; }
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

    /// <summary>Made a control account (posted only by documents, each line with a party). Only while unused.</summary>
    public void MarkAsControl()
    {
        IsControl = true;
        Version++;
    }

    /// <summary>Records a change that belongs to the account, such as its usages, so concurrent edits conflict.</summary>
    public void Touch() => Version++;

    /// <summary>Called when bank details are attached (one bank account per ledger account).</summary>
    public void MarkAsBank()
    {
        IsBank = true;
        Version++;
    }

    /// <summary>Only active accounts take postings; groups never do.</summary>
    public bool Postable => Active;

    public static string RequireCode(string? code)
    {
        var trimmed = code?.Trim() ?? "";
        return trimmed.Length is 0 or > MaxCodeLength
            ? throw new DomainException($"code must be 1–{MaxCodeLength} characters.")
            : trimmed;
    }

    /// <summary>Partial update; null arguments leave a field unchanged.</summary>
    public void Update(string? code, string? name, AccountType? type, bool? active, bool groupChanges, AccountGroup? group)
    {
        Type = type ?? Type;
        if (groupChanges)
        {
            PlaceIn(group);
        }
        else if (GroupId is not null && type is not null && group?.Type != Type)
        {
            throw new DomainException("An account must be of the same type as its group; move it to a group of that type.");
        }
        Code = code is null ? Code : RequireCode(code);
        Name = name is null ? Name : Names.Require(name);
        Active = active ?? Active;
        Version++;
    }

    private void PlaceIn(AccountGroup? group)
    {
        if (group is not null && (group.LedgerId != LedgerId || group.Type != Type))
        {
            throw new DomainException("A group must be in the same ledger and of the same type as the account.");
        }
        GroupId = group?.Id;
    }
}
