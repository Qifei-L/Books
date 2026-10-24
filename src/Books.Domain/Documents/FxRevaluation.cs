using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

public enum RevaluationLineKind { Bank, Receivable, Payable }

/// <summary>
/// Year-end revaluation of balances in another currency (M12.2b, owner decision: yearly). Foreign-currency bank
/// accounts and open foreign-currency invoices are restated at the closing rate: banks on the bank account itself
/// (its own-currency amount unchanged), invoices on the revaluation accounts beside the control accounts, so aging
/// still equals them. The difference is unrealized exchange gain or loss, and the posting is reversed the next day,
/// because settling an invoice later realizes its difference from the invoice's own rate.
/// </summary>
public sealed class FxRevaluation
{
    public const string Kind = "fx_revaluation";
    public const string ReversalKind = "fx_revaluation_reversal";

    private readonly List<FxRevaluationLine> _lines = [];

    private FxRevaluation() { }

    public FxRevaluation(Guid id, Guid orgId, Guid ledgerId, long revaluationNo, DateOnly date, string rates, IEnumerable<FxRevaluationLine> lines,
        Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        RevaluationNo = revaluationNo;
        Date = date;
        Rates = rates;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        _lines.AddRange(lines.Select((l, i) => l.Numbered(id, i + 1)));
        if (_lines.Count == 0)
        {
            throw new DomainException("Nothing to revalue: no balances in another currency differ at the closing rate.");
        }
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public long RevaluationNo { get; private set; }

    /// <summary>The closing date, usually the last day of the fiscal year; the reversal is dated the day after.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The closing rates used, as JSON: {"KHR": "4100"} (units per one unit of the ledger's currency).</summary>
    public string Rates { get; private set; } = "{}";

    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public Guid? JournalId { get; private set; }
    public Guid? ReversalJournalId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public IReadOnlyList<FxRevaluationLine> Lines => _lines;

    public void PostedAs(Guid journalId, Guid reversalJournalId)
    {
        JournalId = journalId;
        ReversalJournalId = reversalJournalId;
        Status = DocumentStatus.Posted;
    }

    public void Delete()
    {
        Status = DocumentStatus.Deleted;
        Version++;
    }

    /// <summary>Each line's adjustment against the unrealized account; banks keep their own-currency amount (zero on these lines).</summary>
    public JournalDraft ToJournal(Guid fxUnrealizedAccount)
    {
        var text = $"Exchange revaluation at {Date:yyyy-MM-dd}";
        var lines = _lines.Select(l => new DraftLine(l.AccountId, l.Adjustment, l.Description, PartyId: l.PartyId, CurrencyAmount: l.Kind == RevaluationLineKind.Bank ? 0 : null))
            .ToList();
        lines.Add(new DraftLine(fxUnrealizedAccount, -lines.Sum(l => l.Amount), text));
        return new JournalDraft(Date, lines, $"FXR-{RevaluationNo}", text);
    }

    /// <summary>The same lines the other way, the day after.</summary>
    public JournalDraft ToReversal(Guid fxUnrealizedAccount)
    {
        var posting = ToJournal(fxUnrealizedAccount);
        return new JournalDraft(Date.AddDays(1), posting.Lines.Select(l => l with { Amount = -l.Amount, CurrencyAmount = l.CurrencyAmount is null ? null : 0 }).ToList(),
            $"FXR-{RevaluationNo}-R", $"Reversal of exchange revaluation at {Date:yyyy-MM-dd}");
    }
}

public sealed class FxRevaluationLine
{
    private FxRevaluationLine() { }

    /// <param name="accountId">The bank account, or the receivable or payable revaluation account.</param>
    /// <param name="foreignAmount">The balance in its own currency (signed as in the ledger: debit positive).</param>
    /// <param name="before">Its value in the ledger's currency before revaluation.</param>
    /// <param name="after">Its value at the closing rate.</param>
    public FxRevaluationLine(RevaluationLineKind kind, Guid accountId, Guid? partyId, Guid? invoiceId, string? description, string currency,
        decimal foreignAmount, decimal before, decimal after)
    {
        Kind = kind;
        AccountId = accountId;
        PartyId = partyId;
        InvoiceId = invoiceId;
        Description = description;
        Currency = currency;
        ForeignAmount = foreignAmount;
        Before = before;
        After = after;
    }

    public Guid RevaluationId { get; private set; }
    public int LineNo { get; private set; }
    public RevaluationLineKind Kind { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid? PartyId { get; private set; }
    public Guid? InvoiceId { get; private set; }
    public string? Description { get; private set; }
    public string Currency { get; private set; } = "";
    public decimal ForeignAmount { get; private set; }
    public decimal Before { get; private set; }
    public decimal After { get; private set; }

    /// <summary>Debit positive, in the ledger's currency.</summary>
    public decimal Adjustment => After - Before;

    internal FxRevaluationLine Numbered(Guid revaluationId, int lineNo)
    {
        RevaluationId = revaluationId;
        LineNo = lineNo;
        return this;
    }
}
