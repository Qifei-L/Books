using Books.Domain.Enums;

namespace Books.Domain.Entities;

public class Account
{
    public int Id { get; set; }
    public int EntityId { get; set; }
    public Entity Entity { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsSystemReserved { get; set; }
    public bool AllowManualJournal { get; set; } = true;
    public List<JournalLine> JournalLines { get; set; } = [];
}
