using Books.Domain.Access;

namespace Books.Domain.Documents;

/// <summary>
/// A bank statement imported for one bank account (owner decision: AI imports statements through MCP, no file
/// parsers or import screens). Its lines either clear a matching posting or become a draft payment or receipt to
/// Suspense for the accountant or AI to reclassify. The original file is not kept.
/// </summary>
public sealed class BankStatement
{
    private readonly List<BankStatementLine> _lines = [];

    private BankStatement() { }

    public BankStatement(Guid id, Guid orgId, Guid ledgerId, Guid accountId, decimal openingBalance, decimal closingBalance,
        IEnumerable<BankStatementLine> lines, Guid importedBy, DateTimeOffset importedAt)
    {
        var all = lines.ToList();
        if (all.Count is 0 or > 2000)
        {
            throw new DomainException("A statement needs 1–2000 lines.");
        }
        if (openingBalance + all.Sum(l => l.Amount) != closingBalance)
        {
            throw new DomainException($"Opening balance {openingBalance} plus the lines ({all.Sum(l => l.Amount)}) does not equal the closing balance {closingBalance}; check the lines read from the statement.");
        }
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        AccountId = accountId;
        OpeningBalance = openingBalance;
        ClosingBalance = closingBalance;
        FromDate = all.Min(l => l.Date);
        ToDate = all.Max(l => l.Date);
        ImportedBy = importedBy;
        ImportedAt = importedAt;
        _lines.AddRange(all.Select((l, i) => l.Numbered(id, i + 1)));
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal OpeningBalance { get; private set; }
    public decimal ClosingBalance { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public Guid ImportedBy { get; private set; }
    public DateTimeOffset ImportedAt { get; private set; }

    public IReadOnlyList<BankStatementLine> Lines => _lines;
}

public enum StatementLineStatus { Matched, Drafted, Duplicate }

/// <summary>One statement line; positive amounts are money in.</summary>
public sealed class BankStatementLine
{
    private BankStatementLine() { }

    public BankStatementLine(DateOnly date, string? description, string? reference, decimal amount, string? externalId)
    {
        if (amount == 0)
        {
            throw new DomainException("A statement line needs a non-zero amount.");
        }
        Date = date;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Amount = amount;
        ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim();
    }

    public Guid StatementId { get; private set; }
    public int LineNo { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Description { get; private set; }
    public string? Reference { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>The bank's own transaction ID, when the statement has one; used to skip lines imported before.</summary>
    public string? ExternalId { get; private set; }

    public StatementLineStatus Status { get; private set; }

    /// <summary>Matched: the journal cleared. Drafted: the draft payment or receipt made for it.</summary>
    public Guid? JournalId { get; private set; }

    public Guid? PaymentId { get; private set; }

    public void Matched(Guid journalId)
    {
        Status = StatementLineStatus.Matched;
        JournalId = journalId;
    }

    public void Drafted(Guid paymentId)
    {
        Status = StatementLineStatus.Drafted;
        PaymentId = paymentId;
    }

    public void Duplicate() => Status = StatementLineStatus.Duplicate;

    internal BankStatementLine Numbered(Guid statementId, int lineNo)
    {
        StatementId = statementId;
        LineNo = lineNo;
        return this;
    }
}
