using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Journals;

public sealed record JournalLineDto(Guid LineId, Guid AccountId, string Debit, string Credit, string? Description);

/// <param name="Status">Status of the revision shown: draft, error, posted or deleted.</param>
public sealed record JournalDto(
    Guid Id, Guid LedgerId, long JournalNo, string Status, bool Posted, DateOnly AccountingDate, string? Reference,
    string? Description, IReadOnlyList<JournalLineDto> Lines, IReadOnlyList<JournalIssue> Issues, int RevisionNo,
    Guid CreatedBy, DateTimeOffset CreatedAt, string Version);

/// <summary>
/// Save posts in one action; Save Draft keeps the journal without accounting effect (rule 25). A Save that fails
/// validation still commits an Error revision with its issues, and nothing is posted (rule 26).
/// </summary>
public sealed class JournalService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, JournalDraft draft, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "journal.save" : "journal.save_draft", orgId, meta), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var issues = JournalValidator.Validate(draft, await ContextAsync(ledger, draft, null, ct));
            var status = !post ? RevisionStatus.Draft : issues.Count == 0 ? RevisionStatus.Posted : RevisionStatus.Error;

            var journal = new Journal(Guid.CreateVersion7(), orgId, ledgerId, ledger.IssueJournalNo(), principal.UserId, scope.Now);
            var revision = AddRevision(journal, draft, status, issues, principal.UserId, scope.Now);
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
            db.JournalLines.Add(new JournalLine(revision, i + 1, line.LineId ?? Guid.CreateVersion7(), line.AccountId, Trim(line.Description), line.Amount));
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
        return new JournalContext(Currencies.Require(ledger.BaseCurrency).MinorUnits, ledger.ClosedThrough, accounts, previousDate);
    }

    internal async Task<JournalDto> LoadAsync(Guid journalId, Guid revisionId, Ledger ledger, CancellationToken ct)
    {
        var journal = await db.Journals.AsNoTracking().SingleAsync(j => j.Id == journalId, ct);
        var revision = await db.JournalRevisions.AsNoTracking().SingleAsync(r => r.Id == revisionId, ct);
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var lines = await db.JournalLines.AsNoTracking().Where(l => l.RevisionId == revisionId).OrderBy(l => l.LineNo).ToListAsync(ct);
        var issues = revision.Issues is null ? [] : System.Text.Json.JsonSerializer.Deserialize<List<JournalIssue>>(revision.Issues, BooksJson.Options)!;
        return new JournalDto(
            journal.Id, journal.LedgerId, journal.JournalNo, Text.Of(revision.Status), journal.IsPosted, revision.AccountingDate,
            revision.Reference, revision.Description,
            lines.Select(l => new JournalLineDto(l.LineId, l.AccountId, Format(Math.Max(l.Amount, 0), minor), Format(Math.Max(-l.Amount, 0), minor), l.Description)).ToList(),
            issues, revision.RevisionNo, journal.CreatedBy, journal.CreatedAt, journal.Version.ToString());
    }

    private static string Format(decimal amount, int minorUnits) => amount.ToString("F" + minorUnits, CultureInfo.InvariantCulture);

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
