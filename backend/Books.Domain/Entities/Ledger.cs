using Books.Domain.Enums;

namespace Books.Domain.Entities;

public class Ledger
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public Entity Entity { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LedgerType LedgerType { get; set; } = LedgerType.Main;
    public bool IsActive { get; set; } = true;
    public bool AllowDeletePostedJournal { get; set; } = true;
    public List<JournalEntry> JournalEntries { get; set; } = [];
}
