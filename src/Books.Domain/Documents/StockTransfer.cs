using Books.Domain.Access;

namespace Books.Domain.Documents;

/// <summary>
/// Stock moved from one location to another (M10.9). With one average cost per item across locations (owner
/// decision) a transfer changes quantities per location but no value, so it posts no journal.
/// </summary>
public sealed class StockTransfer
{
    public const string Kind = "stock_transfer";

    private readonly List<StockTransferLine> _lines = [];

    private StockTransfer() { }

    public StockTransfer(Guid id, Guid orgId, Guid ledgerId, long transferNo, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        TransferNo = transferNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public long TransferNo { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid FromLocationId { get; private set; }
    public Guid ToLocationId { get; private set; }
    public string? Reference { get; private set; }
    public string? Description { get; private set; }

    /// <summary>Posted (it counts) or deleted; a transfer has no draft, since it posts no journal.</summary>
    public DocumentStatus Status { get; private set; } = DocumentStatus.Posted;

    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public IReadOnlyList<StockTransferLine> Lines => _lines;

    public void Set(DateOnly date, Guid from, Guid to, string? reference, string? description, IEnumerable<StockTransferLine> lines, bool isNew)
    {
        var list = lines.ToList();
        if (from == to)
        {
            throw new DomainException("A transfer needs two different locations.");
        }
        if (list.Count == 0 || list.Any(l => l.Quantity <= 0) || list.GroupBy(l => l.ItemId).Any(g => g.Count() > 1))
        {
            throw new DomainException("A transfer needs lines with a quantity above zero, each item once.");
        }
        Date = date;
        FromLocationId = from;
        ToLocationId = to;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _lines.Clear();
        _lines.AddRange(list.Select((l, i) => l.Numbered(Id, i + 1)));
        if (!isNew)
        {
            Version++;
        }
    }

    public void Delete()
    {
        Status = DocumentStatus.Deleted;
        Version++;
    }
}

public sealed class StockTransferLine
{
    private StockTransferLine() { }

    /// <param name="quantity">In the item's base unit.</param>
    public StockTransferLine(Guid itemId, decimal quantity, string? description)
    {
        ItemId = itemId;
        Quantity = quantity;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid TransferId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public string? Description { get; private set; }

    internal StockTransferLine Numbered(Guid transferId, int lineNo)
    {
        TransferId = transferId;
        LineNo = lineNo;
        return this;
    }
}
