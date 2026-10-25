using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

/// <summary>
/// Changes inventory outside purchases and sales (M10.3, Manager.io style): stock written off, damaged or found,
/// count differences, and revaluations of what stock is worth. It works like a journal: each item line changes an
/// item's quantity and/or value on its inventory account, and the other side is either each line's account (by
/// default the item's cost of sales) or one or more entries on any accounts, which must balance the item lines.
/// </summary>
public sealed class StockAdjustment
{
    public const string Kind = "stock_adjustment";

    private readonly List<StockAdjustmentLine> _lines = [];
    private readonly List<StockAdjustmentEntry> _entries = [];

    private StockAdjustment() { }

    public StockAdjustment(Guid id, Guid orgId, Guid ledgerId, long adjustmentNo, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        AdjustmentNo = adjustmentNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public long AdjustmentNo { get; private set; }
    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public string? Description { get; private set; }
    public Guid? JournalId { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public string? Issues { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public IReadOnlyList<StockAdjustmentLine> Lines => _lines;

    /// <summary>The other side when it is given as entries on accounts, like journal lines; empty when each item line has its account.</summary>
    public IReadOnlyList<StockAdjustmentEntry> Entries => _entries;

    public void Set(DateOnly date, string? reference, string? description, IReadOnlyList<StockAdjustmentLine> lines, IReadOnlyList<StockAdjustmentEntry> entries, bool isNew)
    {
        if (lines.Count == 0)
        {
            throw new DomainException("A stock adjustment needs at least one item line.");
        }
        if (entries.Count > 0)
        {
            if (lines.Any(l => l.AccountId is not null))
            {
                throw new DomainException("Give the other side either as each line's account or as entries, not both.");
            }
            var difference = lines.Sum(l => l.Value) + entries.Sum(e => e.Amount);
            if (difference != 0)
            {
                throw new DomainException($"The entries must balance the item lines; they differ by {difference}.");
            }
        }
        else if (lines.Any(l => l.AccountId is null))
        {
            throw new DomainException("Each item line needs its account when no entries are given.");
        }
        Date = date;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _lines.Clear();
        _lines.AddRange(lines.Select((l, i) => l.Numbered(Id, i + 1)));
        _entries.Clear();
        _entries.AddRange(entries.Select((e, i) => e.Numbered(Id, i + 1)));
        if (!isNew)
        {
            Version++;
        }
    }

    public void PostedAs(Guid journalId)
    {
        JournalId = journalId;
        Status = DocumentStatus.Posted;
        Issues = null;
    }

    public void KeptAsDraft(string? issues) => Issues = issues;

    public void Delete()
    {
        Status = DocumentStatus.Deleted;
        Version++;
    }

    /// <summary>
    /// Each item line's value on its inventory account (debit when it goes up); the other side is the entries, or
    /// each line's account the opposite way.
    /// </summary>
    public JournalDraft ToJournal()
    {
        var text = Description ?? (AdjustmentNo == 0 ? "Stock adjustment" : $"Stock adjustment {AdjustmentNo}");
        var lines = new List<DraftLine>();
        foreach (var l in _lines.Where(l => l.Value != 0))
        {
            var lineText = l.Description ?? text;
            lines.Add(new DraftLine(l.InventoryAccountId, l.Value, lineText, ItemId: l.ItemId, Quantity: l.Quantity == 0 ? null : l.Quantity));
            if (l.AccountId is { } account)
            {
                lines.Add(new DraftLine(account, -l.Value, lineText, ItemId: l.ItemId, Quantity: l.Quantity == 0 ? null : l.Quantity));
            }
        }
        lines.AddRange(_entries.Where(e => e.Amount != 0).Select(e => new DraftLine(e.AccountId, e.Amount, e.Description ?? text)));
        return new JournalDraft(Date, lines, Reference, text);
    }
}

public sealed class StockAdjustmentLine
{
    private StockAdjustmentLine() { }

    /// <param name="quantity">Signed change: positive comes in, negative goes out, zero for a change of value only.</param>
    /// <param name="counted">The counted quantity when the line comes from a stock count; null otherwise.</param>
    /// <param name="bookQuantity">Quantity on hand before this adjustment, on its date.</param>
    /// <param name="value">Signed change of value in the ledger's currency.</param>
    /// <param name="accountId">The other side for this line; null when the adjustment gives entries instead.</param>
    public StockAdjustmentLine(Guid itemId, string? description, decimal quantity, decimal? counted, decimal bookQuantity, decimal? unitCost, decimal value,
        Guid? accountId, Guid inventoryAccountId)
    {
        if (quantity == 0 && value == 0)
        {
            throw new DomainException("Each line must change the quantity or the value; leave out items whose count equals the book quantity.");
        }
        if (quantity != 0 && value != 0 && Math.Sign(value) != Math.Sign(quantity))
        {
            throw new DomainException("When both change, the value must go the same way as the quantity.");
        }
        ItemId = itemId;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Quantity = quantity;
        CountedQuantity = counted;
        BookQuantity = bookQuantity;
        UnitCost = unitCost;
        Value = value;
        AccountId = accountId;
        InventoryAccountId = inventoryAccountId;
    }

    public Guid AdjustmentId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ItemId { get; private set; }
    public string? Description { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal? CountedQuantity { get; private set; }
    public decimal BookQuantity { get; private set; }

    /// <summary>The cost entered for stock coming in; null when the average cost or an amount was used.</summary>
    public decimal? UnitCost { get; private set; }
    public decimal Value { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid InventoryAccountId { get; private set; }

    internal StockAdjustmentLine Numbered(Guid adjustmentId, int lineNo)
    {
        AdjustmentId = adjustmentId;
        LineNo = lineNo;
        return this;
    }
}

/// <summary>One entry of the other side, like a journal line: debit positive, credit negative, in the ledger's currency.</summary>
public sealed class StockAdjustmentEntry
{
    private StockAdjustmentEntry() { }

    public StockAdjustmentEntry(Guid accountId, decimal amount, string? description)
    {
        if (amount == 0)
        {
            throw new DomainException("An entry needs a debit or a credit.");
        }
        AccountId = accountId;
        Amount = amount;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid AdjustmentId { get; private set; }
    public int LineNo { get; private set; }
    public Guid AccountId { get; private set; }
    public decimal Amount { get; private set; }
    public string? Description { get; private set; }

    internal StockAdjustmentEntry Numbered(Guid adjustmentId, int lineNo)
    {
        AdjustmentId = adjustmentId;
        LineNo = lineNo;
        return this;
    }
}
