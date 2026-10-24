namespace Books.Domain.Documents;

/// <summary>
/// A bank posting marked as cleared: it appears on the bank statement (owner decision, Manager.io style). The key is
/// the journal and the bank account, since a transfer touches two bank accounts. A cleared posting is soft-locked:
/// unclear it before changing or deleting what posted it (P9).
/// </summary>
public sealed class BankClearing
{
    private BankClearing() { }

    public BankClearing(Guid orgId, Guid ledgerId, Guid accountId, Guid journalId, DateOnly clearedOn, Guid clearedBy, DateTimeOffset clearedAt)
    {
        OrgId = orgId;
        LedgerId = ledgerId;
        AccountId = accountId;
        JournalId = journalId;
        ClearedOn = clearedOn;
        ClearedBy = clearedBy;
        ClearedAt = clearedAt;
    }

    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid JournalId { get; private set; }

    /// <summary>The statement date the posting cleared on.</summary>
    public DateOnly ClearedOn { get; private set; }

    public Guid ClearedBy { get; private set; }
    public DateTimeOffset ClearedAt { get; private set; }

    public void Reclear(DateOnly clearedOn, Guid by, DateTimeOffset at)
    {
        ClearedOn = clearedOn;
        ClearedBy = by;
        ClearedAt = at;
    }
}
