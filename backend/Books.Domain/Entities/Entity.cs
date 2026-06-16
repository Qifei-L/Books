namespace Books.Domain.Entities;

public class Entity
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<Account> Accounts { get; set; } = [];
    public List<Ledger> Ledgers { get; set; } = [];
}
