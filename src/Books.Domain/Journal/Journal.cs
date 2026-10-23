namespace Books.Domain.Journals;

public enum JournalKind { Manual, Opening }

/// <summary>
/// Draft: saved without accounting effect. Error: a Save that failed validation, kept with its issues.
/// Posted: the effective accounting. Superseded: replaced by a later revision. Deleted: the journal was removed.
/// </summary>
public enum RevisionStatus { Draft, Error, Posted, Superseded, Deleted }

/// <summary>
/// A journal in one ledger. Its content lives in immutable revisions; <see cref="EffectiveRevisionId"/> points at
/// the posted revision that reports read (rule 37), or is null when nothing is posted.
/// </summary>
public sealed class Journal
{
    private Journal() { }

    public Journal(Guid id, Guid orgId, Guid ledgerId, long journalNo, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        JournalNo = journalNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>System-generated, unique per ledger (rule 24). Users keep their own numbering in the revision's Reference.</summary>
    public long JournalNo { get; private set; }

    public Guid? EffectiveRevisionId { get; private set; }

    /// <summary>The newest revision of any status; the one an editor sees.</summary>
    public Guid? CurrentRevisionId { get; private set; }

    public int RevisionCount { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public bool IsPosted => EffectiveRevisionId is not null;

    /// <summary>Records a new revision; a posted one becomes effective, a deletion clears the effective revision.</summary>
    public void Apply(JournalRevision revision)
    {
        CurrentRevisionId = revision.Id;
        if (revision.Status == RevisionStatus.Posted)
        {
            EffectiveRevisionId = revision.Id;
        }
        else if (revision.Status == RevisionStatus.Deleted)
        {
            EffectiveRevisionId = null;
        }
        if (RevisionCount > 0)
        {
            Version++;
        }
        RevisionCount++;
    }
}

/// <summary>One saved version of a journal. Never changed after it is written, except to mark it superseded.</summary>
public sealed class JournalRevision
{
    private JournalRevision() { }

    public JournalRevision(
        Guid id, Journal journal, int revisionNo, JournalKind kind, RevisionStatus status, DateOnly accountingDate,
        string? reference, string? description, string? issues, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = journal.OrgId;
        LedgerId = journal.LedgerId;
        JournalId = journal.Id;
        RevisionNo = revisionNo;
        Kind = kind;
        Status = status;
        AccountingDate = accountingDate;
        Reference = reference;
        Description = description;
        Issues = issues;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid JournalId { get; private set; }
    public int RevisionNo { get; private set; }
    public JournalKind Kind { get; private set; }
    public RevisionStatus Status { get; private set; }
    public DateOnly AccountingDate { get; private set; }

    /// <summary>Free text the user chooses (for example their own voucher number); may repeat (rule 24).</summary>
    public string? Reference { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Validation issues as JSON for Error revisions; null otherwise.</summary>
    public string? Issues { get; private set; }

    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }

    public void Supersede(DateTimeOffset at)
    {
        Status = RevisionStatus.Superseded;
        SupersededAt = at;
    }
}

/// <summary>
/// A journal line in the ledger's base currency (owner decision: base currency only in v1).
/// One signed amount: debit positive, credit negative. <see cref="LineId"/> stays the same across revisions.
/// </summary>
public sealed class JournalLine
{
    private JournalLine() { }

    public JournalLine(JournalRevision revision, int lineNo, Guid lineId, Guid accountId, string? description, decimal amount, Guid? partyId = null)
    {
        RevisionId = revision.Id;
        OrgId = revision.OrgId;
        LedgerId = revision.LedgerId;
        LineNo = lineNo;
        LineId = lineId;
        AccountId = accountId;
        Description = description;
        Amount = amount;
        PartyId = partyId;
    }

    public Guid RevisionId { get; private set; }
    public int LineNo { get; private set; }
    public Guid LineId { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public Guid AccountId { get; private set; }

    /// <summary>Optional counterparty; required on control accounts (rule 18).</summary>
    public Guid? PartyId { get; private set; }

    public string? Description { get; private set; }
    public decimal Amount { get; private set; }
}
