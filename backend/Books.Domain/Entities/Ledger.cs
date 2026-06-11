namespace Books.Domain.Entities;

public class Ledger
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<Account> Accounts { get; set; } = [];
    public List<JournalEntry> JournalEntries { get; set; } = [];
}
