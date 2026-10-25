using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

/// <summary>
/// An assembly (M10.10, Manager.io's production order): a quantity of a finished item made from its components
/// (its bill of materials). Components leave stock at their average cost; the finished item comes in at their total
/// plus labour (owner decision), the labour being credited to the account it was paid from or accrued in.
/// </summary>
public sealed class Assembly
{
    public const string Kind = "assembly";

    private readonly List<AssemblyLine> _lines = [];

    private Assembly() { }

    public Assembly(Guid id, Guid orgId, Guid ledgerId, long assemblyNo, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        AssemblyNo = assemblyNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public long AssemblyNo { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid ItemId { get; private set; }
    public decimal Quantity { get; private set; }
    public Guid InventoryAccountId { get; private set; }
    public decimal LabourAmount { get; private set; }
    public Guid? LabourAccountId { get; private set; }
    public Guid? LocationId { get; private set; }
    public string? Reference { get; private set; }
    public string? Description { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public Guid? JournalId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public IReadOnlyList<AssemblyLine> Lines => _lines;

    /// <summary>The finished item's value: the components' cost plus labour.</summary>
    public decimal Value => _lines.Sum(l => l.Value) + LabourAmount;

    public void Set(DateOnly date, Guid itemId, decimal quantity, Guid inventoryAccountId, decimal labour, Guid? labourAccount, Guid? locationId,
        string? reference, string? description, IEnumerable<AssemblyLine> lines)
    {
        if (quantity <= 0)
        {
            throw new DomainException("quantity must be above zero.");
        }
        if (labour < 0 || (labour > 0) != (labourAccount is not null))
        {
            throw new DomainException("Labour needs an account, and a labour account needs an amount above zero.");
        }
        Date = date;
        ItemId = itemId;
        Quantity = quantity;
        InventoryAccountId = inventoryAccountId;
        LabourAmount = labour;
        LabourAccountId = labourAccount;
        LocationId = locationId;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _lines.Clear();
        _lines.AddRange(lines.Select((l, i) => l.Numbered(Id, i + 1)));
        if (_lines.Count == 0)
        {
            throw new DomainException("The item has no components; set its bill of materials first.");
        }
    }

    public void PostedAs(Guid journalId)
    {
        JournalId = journalId;
        Status = DocumentStatus.Posted;
    }

    public void Delete()
    {
        Status = DocumentStatus.Deleted;
        Version++;
    }

    /// <summary>Dr the finished item's inventory account; Cr each component's inventory account and the labour account.</summary>
    public JournalDraft ToJournal()
    {
        var text = Description ?? $"Assembly {AssemblyNo}";
        var lines = new List<DraftLine> { new(InventoryAccountId, Value, text, ItemId: ItemId, Quantity: Quantity) };
        lines.AddRange(_lines.Where(l => l.Value != 0).Select(l => new DraftLine(l.InventoryAccountId, -l.Value, text, ItemId: l.ComponentId, Quantity: l.Quantity)));
        if (LabourAccountId is { } labour)
        {
            lines.Add(new DraftLine(labour, -LabourAmount, $"{text} (labour)"));
        }
        return new JournalDraft(Date, lines, Reference, text);
    }
}

public sealed class AssemblyLine
{
    private AssemblyLine() { }

    /// <param name="quantity">The component used, in its base unit.</param>
    /// <param name="value">Its cost at the average cost on the date.</param>
    public AssemblyLine(Guid componentId, decimal quantity, decimal value, Guid inventoryAccountId)
    {
        ComponentId = componentId;
        Quantity = quantity;
        Value = value;
        InventoryAccountId = inventoryAccountId;
    }

    public Guid AssemblyId { get; private set; }
    public int LineNo { get; private set; }
    public Guid ComponentId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Value { get; private set; }
    public Guid InventoryAccountId { get; private set; }

    internal AssemblyLine Numbered(Guid assemblyId, int lineNo)
    {
        AssemblyId = assemblyId;
        LineNo = lineNo;
        return this;
    }
}
