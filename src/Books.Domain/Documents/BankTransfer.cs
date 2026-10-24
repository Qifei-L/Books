using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

/// <summary>
/// Draft: saved without accounting effect, either by choice or because posting failed (with the reasons in
/// Issues). Posted: its journal is effective. Deleted: removed, history kept.
/// </summary>
public enum DocumentStatus { Draft, Posted, Deleted }

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
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;

    /// <summary>Why the document is not posted (JSON validation issues); null when posted or when none were found.</summary>
    public string? Issues { get; private set; }

    /// <summary>Between accounts in different currencies: what arrives, in the receiving account's currency (M12.2); null otherwise.</summary>
    public decimal? ToAmount { get; private set; }

    /// <summary>When the sending account is in another currency than the ledger's: its units per one unit of the ledger's.</summary>
    public decimal? FromRate { get; private set; }

    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Sets the content; account kinds (bank, expense) and currencies are checked by the caller against the ledger.</summary>
    public void Set(DateOnly date, Guid from, Guid to, decimal amount, decimal fee, Guid? feeAccount, string? reference, string? description, bool isNew,
        decimal? toAmount = null, decimal? fromRate = null)
    {
        if (toAmount is <= 0)
        {
            throw new DomainException("to_amount must be positive.");
        }
        ToAmount = toAmount;
        FromRate = fromRate;
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

    public void PostedAs(Guid journalId)
    {
        JournalId = journalId;
        Status = DocumentStatus.Posted;
        Issues = null;
    }

    /// <summary>Kept as a draft, with the reasons it could not be posted (or none when saved as a draft on purpose).</summary>
    public void KeptAsDraft(string? issues) => Issues = issues;

    public void Delete()
    {
        Status = DocumentStatus.Deleted;
        Version++;
    }

    /// <summary>
    /// Posting adapter: Dr to-bank amount · Dr fee account fee · Cr from-bank amount + fee. The bank pays the fee
    /// from the sending account. Between currencies (M12.2) the sending side is converted at <see cref="FromRate"/>;
    /// the receiving side takes the same value, or its own amount when it is in the ledger's currency, and the
    /// difference is realized exchange gain or loss. Lines on a foreign-currency account carry that currency's amount.
    /// </summary>
    /// <param name="fromForeign">The sending account is in another currency than the ledger's.</param>
    /// <param name="toForeign">The receiving account is in another currency than the ledger's.</param>
    public JournalDraft ToJournal(int minorUnits = 2, bool fromForeign = false, bool toForeign = false, Guid? fxRealizedAccount = null)
    {
        decimal B(decimal amount) => FromRate is { } rate ? Math.Round(amount / rate, minorUnits, MidpointRounding.AwayFromZero) : amount;
        var text = Description ?? $"Transfer {TransferNo}";
        var arrives = ToAmount ?? Amount;
        var fromBase = B(Amount);
        var toBase = toForeign ? fromBase : arrives;
        var lines = new List<DraftLine> { new(ToAccountId, toBase, text, CurrencyAmount: toForeign ? arrives : null) };
        if (FeeAccountId is { } feeAccount)
        {
            lines.Add(new DraftLine(feeAccount, B(FeeAmount), "Bank fee"));
        }
        if (toBase != fromBase)
        {
            lines.Add(new DraftLine(fxRealizedAccount ?? throw new DomainException("A transfer between currencies needs an account with the FX realized usage."),
                fromBase - toBase, $"{text} (exchange difference)"));
        }
        lines.Add(new DraftLine(FromAccountId, -lines.Sum(l => l.Amount), text, CurrencyAmount: fromForeign ? -(Amount + FeeAmount) : null));
        return new JournalDraft(Date, lines, Reference, text);
    }
}
