using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

/// <summary>
/// Changes inventory quantities outside purchases and sales (M10.3, Manager.io's inventory write-off): stock written
/// off, damaged or found, and the differences from a stock count. Each line moves one item in or out at a value the
/// service works out from the stock on the date (average cost going out; the entered or average cost coming in),
/// against another account, usually cost of sales.
/// </summary>
public sealed class StockAdjustment
{
    public const string Kind = "stock_adjustment";

    private readonly List<StockAdjustmentLine> _lines = [];

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

    public void Set(DateOnly date, string? reference, string? description, IReadOnlyList<StockAdjustmentLine> lines, bool isNew)
    {
        if (lines.Count == 0)
        {
            throw new DomainException("A stock adjustment needs at least one line.");
        }
        Date = date;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _lines.Clear();
        _lines.AddRange(lines.Select((l, i) => l.Numbered(Id, i + 1)));
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

    /// <summary>Per line: Dr inventory / Cr the line's account for stock coming in, the opposite going out.</summary>
    public JournalDraft ToJournal()
    {
        var text = Description ?? $"Stock adjustment {AdjustmentNo}";
        var lines = new List<DraftLine>();
        foreach (var l in _lines.Where(l => l.Value != 0))
        {
            var lineText = l.Description ?? text;
            lines.Add(new DraftLine(l.InventoryAccountId, l.Value, lineText, ItemId: l.ItemId, Quantity: l.Quantity));
            lines.Add(new DraftLine(l.AccountId, -l.Value, lineText, ItemId: l.ItemId, Quantity: l.Quantity));
        }
        return new JournalDraft(Date, lines, Reference, text);
    }
}

public sealed class StockAdjustmentLine
{
    private StockAdjustmentLine() { }

    /// <param name="quantity">Signed change: positive comes in, negative goes out.</param>
    /// <param name="counted">The counted quantity when the line comes from a stock count; null otherwise.</param>
    /// <param name="bookQuantity">Quantity on hand before this adjustment, on its date.</param>
    /// <param name="value">Signed value in the ledger's currency, the same sign as the quantity.</param>
    public StockAdjustmentLine(Guid itemId, string? description, decimal quantity, decimal? counted, decimal bookQuantity, decimal? unitCost, decimal value,
        Guid accountId, Guid inventoryAccountId)
    {
        if (quantity == 0)
        {
            throw new DomainException("Each line must change the quantity; leave out items whose count equals the book quantity.");
        }
        if (Math.Sign(value) == -Math.Sign(quantity))
        {
            throw new DomainException("A line's value must have the same sign as its quantity.");
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

    /// <summary>The cost entered for stock coming in; null when the average cost was used.</summary>
    public decimal? UnitCost { get; private set; }
    public decimal Value { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid InventoryAccountId { get; private set; }

    internal StockAdjustmentLine Numbered(Guid adjustmentId, int lineNo)
    {
        AdjustmentId = adjustmentId;
        LineNo = lineNo;
        return this;
    }
}
