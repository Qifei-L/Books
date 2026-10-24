using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <summary>Either the posted journal, or the issues that stopped the posting (nothing was posted).</summary>
public sealed record PostingOutcome(Journal? Journal, IReadOnlyList<JournalIssue> Issues);

/// <summary>
/// The document framework. A document service builds its journal lines with a posting adapter and calls this
/// inside its own write transaction, so the document and its journal are saved together or not at all (rule 31).
/// Generated journals are linked to their source and can only change through it (rule 32).
/// </summary>
public sealed class DocumentPosting(IBooksDb db, JournalService journals)
{
    /// <summary>
    /// Posts a new journal for the document, or replaces the posting of its existing journal. When the lines fail
    /// validation nothing is posted and the issues are returned: the caller keeps a new or draft document as a
    /// draft with those reasons, and refuses a failed edit of a posted document (rule 28).
    /// </summary>
    public async Task<PostingOutcome> PostAsync(WriteScope scope, Ledger ledger, string sourceKind, Guid sourceId, Guid? journalId, JournalDraft draft, CancellationToken ct)
    {
        JournalService.RefuseAgentPosting(scope.Principal, touchesPosting: true);
        var journal = journalId is { } id ? await db.Journals.SingleAsync(j => j.Id == id, ct) : null;
        if (journal is not null)
        {
            await BankReconciliationService.EnsureNotClearedAsync(db, journal.Id, ct);
        }
        var previousDate = journal?.EffectiveRevisionId is { } effective
            ? await db.JournalRevisions.Where(r => r.Id == effective).Select(r => (DateOnly?)r.AccountingDate).SingleAsync(ct)
            : null;
        var context = await journals.ContextAsync(ledger, draft, previousDate, ct) with { FromDocument = true };
        var issues = JournalService.RejectUnknownReferences(JournalValidator.Validate(draft, context));
        if (issues.Count > 0)
        {
            return new PostingOutcome(null, issues);
        }

        if (journal is null)
        {
            journal = new Journal(Guid.CreateVersion7(), ledger.OrgId, ledger.Id, ledger.IssueJournalNo(), scope.Principal.UserId, scope.Now);
            journal.LinkTo(sourceKind, sourceId);
            db.Journals.Add(journal);
        }
        else
        {
            await journals.SupersedeAsync(journal, keepEffective: false, scope.Now, ct);
        }
        journals.AddRevision(journal, draft, RevisionStatus.Posted, [], scope.Principal.UserId, scope.Now);
        ledger.RecordPosting(scope.Now);
        return new PostingOutcome(journal, []);
    }

    /// <summary>Validates without posting, for a document saved as a draft on purpose: the reasons it would not post yet.</summary>
    public async Task<IReadOnlyList<JournalIssue>> CheckAsync(Ledger ledger, JournalDraft draft, CancellationToken ct) =>
        JournalService.RejectUnknownReferences(JournalValidator.Validate(draft, await journals.ContextAsync(ledger, draft, null, ct) with { FromDocument = true }));

    /// <summary>Removes the document's posting while its date is after Close; history stays as revisions.</summary>
    public async Task DeleteAsync(WriteScope scope, Ledger ledger, Guid journalId, CancellationToken ct)
    {
        JournalService.RefuseAgentPosting(scope.Principal, touchesPosting: true);
        var journal = await db.Journals.SingleAsync(j => j.Id == journalId, ct);
        await BankReconciliationService.EnsureNotClearedAsync(db, journal.Id, ct);
        var shown = await db.JournalRevisions.SingleAsync(r => r.Id == (journal.EffectiveRevisionId ?? journal.CurrentRevisionId), ct);
        if (ledger.ClosedThrough is { } closed && shown.AccountingDate <= closed)
        {
            throw BooksProblem.Conflict("DATE_CLOSED", $"The books are closed through {closed:yyyy-MM-dd}.");
        }
        await journals.SupersedeAsync(journal, keepEffective: false, scope.Now, ct);
        journals.AddRevision(journal, new JournalDraft(shown.AccountingDate, [], shown.Reference, shown.Description), RevisionStatus.Deleted, [], scope.Principal.UserId, scope.Now);
    }

    /// <summary>Next number for a document type in a ledger. Runs under the organization write lock.</summary>
    public async Task<long> NextNumberAsync(Guid ledgerId, string kind, CancellationToken ct) =>
        (await db.Database.SqlQuery<long>($"""
            INSERT INTO document_counters (ledger_id, kind, last_no) VALUES ({ledgerId}, {kind}, 1)
            ON CONFLICT (ledger_id, kind) DO UPDATE SET last_no = document_counters.last_no + 1
            RETURNING last_no AS "Value"
            """).ToListAsync(ct)).Single();
}
