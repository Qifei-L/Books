using Books.Domain.Enums;

namespace Books.Domain.Entities;

public class JournalEntry
{
    public int Id { get; set; }
    public int LedgerId { get; set; }
    public Ledger Ledger { get; set; } = null!;
    public string JournalNo { get; set; } = string.Empty;
    public DateTime EntryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public JournalStatus Status { get; set; } = JournalStatus.Draft;
    public List<JournalLine> Lines { get; set; } = [];
}
