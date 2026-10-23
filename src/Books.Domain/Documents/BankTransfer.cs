using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

/// <summary>
/// Money moved between two bank accounts of the same ledger, with an optional bank fee (P10). The first document
/// type: it posts through the document framework, so the transfer and its journal always change together
/// (rule 31) and the journal is changed only through the transfer (rule 32).
/// </summary>
public sealed class BankTransfer
{
    public const string Kind = "bank_transfer";

    private BankTransfer() { }

    public BankTransfer(Guid id, Guid orgId, Guid ledgerId, long transferNo, Guid createdBy, DateTimeOffset createdAt)
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

    /// <summary>System number, unique per ledger and document type; the user's own number goes in Reference.</summary>
    public long TransferNo { get; private set; }

    public DateOnly Date { get; private set; }
    public Guid FromAccountId { get; private set; }
    public Guid ToAccountId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal FeeAmount { get; private set; }
    public Guid? FeeAccountId { get; private set; }
    public string? Reference { get; private set; }
    public string? Description { get; private set; }
    public Guid? JournalId { get; private set; }
    public bool Deleted { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Sets the content; account kinds (bank, expense) are checked by the caller against the ledger.</summary>
    public void Set(DateOnly date, Guid from, Guid to, decimal amount, decimal fee, Guid? feeAccount, string? reference, string? description, bool isNew)
    {
        if (from == to)
        {
            throw new DomainException("A transfer needs two different bank accounts.");
        }
        if (amount <= 0)
        {
            throw new DomainException("amount must be positive.");
        }
        if (fee < 0 || (fee > 0) != (feeAccount is not null))
        {
            throw new DomainException("A fee needs a fee account, and a fee account needs a positive fee.");
        }
        Date = date;
        FromAccountId = from;
        ToAccountId = to;
        Amount = amount;
        FeeAmount = fee;
        FeeAccountId = feeAccount;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (!isNew)
        {
            Version++;
        }
    }

    public void PostedAs(Guid journalId) => JournalId = journalId;

    public void Delete()
    {
        Deleted = true;
        Version++;
    }

    /// <summary>
    /// Posting adapter: Dr to-bank amount · Dr fee account fee · Cr from-bank amount + fee. The bank pays the fee
    /// from the sending account.
    /// </summary>
    public JournalDraft ToJournal()
    {
        var text = Description ?? $"Transfer {TransferNo}";
        var lines = new List<DraftLine> { new(ToAccountId, Amount, text) };
        if (FeeAccountId is { } feeAccount)
        {
            lines.Add(new DraftLine(feeAccount, FeeAmount, "Bank fee"));
        }
        lines.Add(new DraftLine(FromAccountId, -(Amount + FeeAmount), text));
        return new JournalDraft(Date, lines, Reference, text);
    }
}
