using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Journals;

public sealed record JournalLineDto(Guid LineId, Guid AccountId, Guid? PartyId, Guid? ItemId, string? Quantity, string Debit, string Credit, string? Description);

/// <param name="Status">Status of the revision shown: draft, error, posted or deleted.</param>
public sealed record JournalDto(
    Guid Id, Guid LedgerId, long JournalNo, string? SourceKind, Guid? SourceId, string Status, bool Posted, DateOnly AccountingDate, string? Reference,
    string? Description, IReadOnlyList<JournalLineDto> Lines, IReadOnlyList<JournalIssue> Issues, int RevisionNo,
    Guid CreatedBy, DateTimeOffset CreatedAt, string Version);

/// <summary>
/// Save posts in one action; Save Draft keeps the journal without accounting effect (rule 25). A Save that fails
/// validation still commits an Error revision with its issues, and nothing is posted (rule 26).
/// </summary>
public sealed class JournalService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, JournalDraft draft, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "journal.save" : "journal.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            RefuseAgentPosting(principal, post);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var issues = RejectUnknownReferences(JournalValidator.Validate(draft, await ContextAsync(ledger, draft, null, ct)));
            var status = !post ? RevisionStatus.Draft : issues.Count == 0 ? RevisionStatus.Posted : RevisionStatus.Error;

            var journal = new Journal(Guid.CreateVersion7(), orgId, ledgerId, ledger.IssueJournalNo(), principal.UserId, scope.Now);
            var revision = AddRevision(journal, draft, status, issues, principal.UserId, scope.Now);
            if (status == RevisionStatus.Posted)
            {
                ledger.RecordPosting(scope.Now);
            }
            db.Journals.Add(journal);
            scope.Audit($"journal.{Text.Of(status)}", "journal", journal.Id, new
            {
                ledger_id = ledgerId,
                journal_no = journal.JournalNo,
                revision_no = revision.RevisionNo,
                issues = issues.Select(i => i.Code),
            });
            await db.SaveChangesAsync(ct);

            var dto = await LoadAsync(journal.Id, revision.Id, ledger, ct);
            return WriteResult.Of(status == RevisionStatus.Error ? 422 : 201, dto, journal.Version);
        }, ct);

    /// <summary>
    /// Saves a new revision of an existing journal. A valid Save replaces the posted effect exactly once; a failed
    /// Save adds an Error revision and leaves the previous posting untouched (rule 28). Both the original and the
    /// new date must be after Close (rule 27). A stale version is rejected, never merged (rule 29).
    /// </summary>
    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid journalId, JournalDraft draft, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "journal.save" : "journal.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            RefuseAgentPosting(principal, post);
            var journal = await FindForWriteAsync(ledgerId, journalId, principal, ct);
            scope.CheckVersion(journal.Version);
            if (!post && journal.IsPosted)
            {
                throw BooksProblem.Conflict("JOURNAL_POSTED", "A posted journal cannot go back to draft; save it or delete it.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var effective = journal.EffectiveRevisionId is { } effectiveId ? await db.JournalRevisions.SingleAsync(r => r.Id == effectiveId, ct) : null;
            var issues = RejectUnknownReferences(JournalValidator.Validate(draft, await ContextAsync(ledger, draft, effective?.AccountingDate, ct)));
            var status = !post ? RevisionStatus.Draft : issues.Count == 0 ? RevisionStatus.Posted : RevisionStatus.Error;

            await SupersedeAsync(journal, keepEffective: status != RevisionStatus.Posted, scope.Now, ct);
            var revision = AddRevision(journal, draft, status, issues, principal.UserId, scope.Now);
            if (status == RevisionStatus.Posted)
            {
                ledger.RecordPosting(scope.Now);
            }
            scope.Audit($"journal.{Text.Of(status)}", "journal", journal.Id, new
            {
                journal_no = journal.JournalNo,
                revision_no = revision.RevisionNo,
                replaced_revision_id = status == RevisionStatus.Posted ? effective?.Id : null,
                issues = issues.Select(i => i.Code),
            });
            await db.SaveChangesAsync(ct);
            var dto = await LoadAsync(journal.Id, revision.Id, ledger, ct);
            return WriteResult.Of(status == RevisionStatus.Error ? 422 : 200, dto, journal.Version);
        }, ct);

    /// <summary>Deletes a journal while its date is after Close (rule 27). History stays as revisions.</summary>
    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid journalId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("journal.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var journal = await FindForWriteAsync(ledgerId, journalId, principal, ct);
            RefuseAgentPosting(principal, journal.IsPosted);
            scope.CheckVersion(journal.Version);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var shown = await db.JournalRevisions.SingleAsync(r => r.Id == (journal.EffectiveRevisionId ?? journal.CurrentRevisionId), ct);
            if (journal.IsPosted && ledger.ClosedThrough is { } closed && shown.AccountingDate <= closed)
            {
                throw BooksProblem.Conflict("DATE_CLOSED", $"The books are closed through {closed:yyyy-MM-dd}.");
            }
            await SupersedeAsync(journal, keepEffective: false, scope.Now, ct);
            var revision = AddRevision(journal, new JournalDraft(shown.AccountingDate, [], shown.Reference, shown.Description),
                RevisionStatus.Deleted, [], principal.UserId, scope.Now);
            scope.Audit("journal.deleted", "journal", journal.Id, new { journal_no = journal.JournalNo, deleted_revision_id = shown.Id });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, new { id = journal.Id, journal_no = journal.JournalNo, deleted = true, revision_no = revision.RevisionNo }, journal.Version);
        }, ct);

    /// <summary>
    /// An account or party that is not in this ledger cannot be stored even in a draft or Error revision, so the
    /// request is rejected as invalid input (400) and nothing is saved.
    /// </summary>
    internal static IReadOnlyList<JournalIssue> RejectUnknownReferences(IReadOnlyList<JournalIssue> issues) =>
        issues.FirstOrDefault(i => i.Code is "UNKNOWN_ACCOUNT" or "UNKNOWN_PARTY" or "UNKNOWN_ITEM") is { } unknown
            ? throw BooksProblem.Invalid($"Line {unknown.Line}: {unknown.Message}")
            : issues;

    /// <summary>AI clients prepare drafts; only a person posts, edits or deletes posted journals (owner decision 2026-10-23).</summary>
    internal static void RefuseAgentPosting(Principal principal, bool touchesPosting)
    {
        if (principal.IsAgent && touchesPosting)
        {
            throw new BooksProblem(403, "AGENT_CANNOT_POST", "AI clients can save drafts only; a person must post them.");
        }
    }

    /// <summary>Unposted journals belong to their creator alone; a deleted journal cannot change again.</summary>
    private async Task<Journal> FindForWriteAsync(Guid ledgerId, Guid journalId, Principal principal, CancellationToken ct)
    {
        var journal = await db.Journals.SingleOrDefaultAsync(j => j.LedgerId == ledgerId && j.Id == journalId, ct);
        if (journal is null || (!journal.IsPosted && journal.CreatedBy != principal.UserId))
        {
            throw BooksProblem.NotFound();
        }
        if (journal.SourceKind is not null)
        {
            throw BooksProblem.Conflict("JOURNAL_FROM_DOCUMENT", $"This journal was generated by a {journal.SourceKind.Replace('_', ' ')}; change the document instead (rule 32).");
        }
        var current = await db.JournalRevisions.AsNoTracking().SingleAsync(r => r.Id == journal.CurrentRevisionId, ct);
        return current.Status == RevisionStatus.Deleted
            ? throw BooksProblem.Conflict("JOURNAL_DELETED", "This journal was deleted.")
            : journal;
    }

    /// <summary>Marks the current revision superseded, and the effective one too unless the posting must stay.</summary>
    internal async Task SupersedeAsync(Journal journal, bool keepEffective, DateTimeOffset now, CancellationToken ct)
    {
        var ids = new[] { journal.CurrentRevisionId, keepEffective ? null : journal.EffectiveRevisionId }
            .OfType<Guid>().Where(id => !keepEffective || id != journal.EffectiveRevisionId).Distinct().ToList();
        foreach (var revision in await db.JournalRevisions.Where(r => ids.Contains(r.Id) && r.Status != RevisionStatus.Superseded).ToListAsync(ct))
        {
            revision.Supersede(now);
        }
    }

    /// <summary>Posted journals for anyone with read access; drafts and failed saves only for their creator.</summary>
    public Task<IReadOnlyList<JournalDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<JournalDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            var journals = await db.Journals.AsNoTracking()
                .Where(j => j.LedgerId == ledgerId && (j.EffectiveRevisionId != null || j.CreatedBy == principal.UserId))
                .OrderByDescending(j => j.JournalNo).Take(200)
                .ToListAsync(ct);
            var result = new List<JournalDto>();
            foreach (var journal in journals)
            {
                result.Add(await LoadAsync(journal.Id, VisibleRevision(journal, principal), ledger, ct));
            }
            return result;
        }, ct);

    public Task<JournalDto> GetAsync(Principal principal, Guid orgId, Guid ledgerId, Guid journalId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var journal = await db.Journals.AsNoTracking().SingleOrDefaultAsync(j => j.LedgerId == ledgerId && j.Id == journalId, ct);
            if (journal is null || (journal.EffectiveRevisionId is null && journal.CreatedBy != principal.UserId))
            {
                throw BooksProblem.NotFound();
            }
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            return await LoadAsync(journal.Id, VisibleRevision(journal, principal), ledger, ct);
        }, ct);

    /// <summary>Others see the posted content; the creator sees their latest revision.</summary>
    private static Guid VisibleRevision(Journal journal, Principal principal) =>
        journal.CreatedBy == principal.UserId || journal.EffectiveRevisionId is null
            ? journal.CurrentRevisionId!.Value
            : journal.EffectiveRevisionId.Value;

    internal JournalRevision AddRevision(Journal journal, JournalDraft draft, RevisionStatus status, IReadOnlyList<JournalIssue> issues, Guid userId, DateTimeOffset now)
    {
        var revision = new JournalRevision(
            Guid.CreateVersion7(), journal, journal.RevisionCount + 1, JournalKind.Manual, status, draft.AccountingDate,
            Trim(draft.Reference), Trim(draft.Description), issues.Count == 0 ? null : BooksJson.Serialize(issues), userId, now);
        db.JournalRevisions.Add(revision);
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var line = draft.Lines[i];
            db.JournalLines.Add(new JournalLine(revision, i + 1, line.LineId ?? Guid.CreateVersion7(), line.AccountId, Trim(line.Description), line.Amount, line.PartyId, line.ItemId, line.Quantity));
        }
        journal.Apply(revision);
        return revision;
    }

    internal async Task<JournalContext> ContextAsync(Ledger ledger, JournalDraft draft, DateOnly? previousDate, CancellationToken ct)
    {
        var accountIds = draft.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.LedgerId == ledger.Id && accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new AccountFacts(a.Id, a.Active, a.IsControl), ct);
        var partyIds = draft.Lines.Where(l => l.PartyId != null).Select(l => l.PartyId!.Value).Distinct().ToList();
        var parties = await db.Parties.AsNoTracking()
            .Where(p => p.LedgerId == ledger.Id && partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Active, ct);
        var itemIds = draft.Lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).Distinct().ToList();
        var items = await db.Items.AsNoTracking()
            .Where(i => i.LedgerId == ledger.Id && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Active, ct);
        return new JournalContext(Currencies.Require(ledger.BaseCurrency).MinorUnits, ledger.ClosedThrough, accounts, previousDate, parties, Items: items);
    }

    internal async Task<JournalDto> LoadAsync(Guid journalId, Guid revisionId, Ledger ledger, CancellationToken ct)
    {
        var journal = await db.Journals.AsNoTracking().SingleAsync(j => j.Id == journalId, ct);
        var revision = await db.JournalRevisions.AsNoTracking().SingleAsync(r => r.Id == revisionId, ct);
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var lines = await db.JournalLines.AsNoTracking().Where(l => l.RevisionId == revisionId).OrderBy(l => l.LineNo).ToListAsync(ct);
        var issues = revision.Issues is null ? [] : System.Text.Json.JsonSerializer.Deserialize<List<JournalIssue>>(revision.Issues, BooksJson.Options)!;
        return new JournalDto(
            journal.Id, journal.LedgerId, journal.JournalNo, journal.SourceKind, journal.SourceId, Text.Of(revision.Status), journal.IsPosted, revision.AccountingDate,
            revision.Reference, revision.Description,
            lines.Select(l => new JournalLineDto(l.LineId, l.AccountId, l.PartyId, l.ItemId, l.Quantity?.ToString("0.####", CultureInfo.InvariantCulture), Format(Math.Max(l.Amount, 0), minor), Format(Math.Max(-l.Amount, 0), minor), l.Description)).ToList(),
            issues, revision.RevisionNo, journal.CreatedBy, journal.CreatedAt, journal.Version.ToString());
    }

    private static string Format(decimal amount, int minorUnits) => amount.ToString("F" + minorUnits, CultureInfo.InvariantCulture);

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
