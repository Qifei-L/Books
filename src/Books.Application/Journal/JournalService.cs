using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Journals;

public sealed record JournalLineDto(Guid LineId, Guid AccountId, Guid? PartyId, Guid? ItemId, string? Quantity, string Debit, string Credit, string? Description,
    string? CurrencyAmount = null, Guid? ClassId = null, Guid? LocationId = null, Guid? TaxCodeId = null, string? TaxAmount = null,
    string? TaxCalculated = null, string? TaxDirection = null, bool TaxLine = false);

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
            var expanded = await ExpandTaxAsync(draft, ledger, ct);
            var context = await ContextAsync(ledger, expanded, null, ct);
            RequireManualControlPermission(scope, expanded, context);
            var issues = RejectUnknownReferences(JournalValidator.Validate(expanded, context));
            var status = !post ? RevisionStatus.Draft : issues.Count == 0 ? RevisionStatus.Posted : RevisionStatus.Error;

            var journal = new Journal(Guid.CreateVersion7(), orgId, ledgerId, ledger.IssueJournalNo(), principal.UserId, scope.Now);
            var revision = AddRevision(journal, expanded, status, issues, principal.UserId, scope.Now);
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
            var expanded = await ExpandTaxAsync(draft, ledger, ct);
            var context = await ContextAsync(ledger, expanded, effective?.AccountingDate, ct);
            RequireManualControlPermission(scope, expanded, context);
            var issues = RejectUnknownReferences(JournalValidator.Validate(expanded, context));
            var status = !post ? RevisionStatus.Draft : issues.Count == 0 ? RevisionStatus.Posted : RevisionStatus.Error;

            await SupersedeAsync(journal, keepEffective: status != RevisionStatus.Posted, scope.Now, ct);
            var revision = AddRevision(journal, expanded, status, issues, principal.UserId, scope.Now);
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
        issues.FirstOrDefault(i => i.Code is "UNKNOWN_ACCOUNT" or "UNKNOWN_PARTY" or "UNKNOWN_ITEM" or "UNKNOWN_CLASS" or "UNKNOWN_LOCATION") is { } unknown
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
        await Documents.BankReconciliationService.EnsureNotClearedAsync(db, journal.Id, ct);
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

    /// <summary>
    /// A manual journal may reach AR/AP only for an organization administrator. Documents post control accounts through
    /// their own adapters and never call this path.
    /// </summary>
    private static void RequireManualControlPermission(WriteScope scope, JournalDraft draft, JournalContext context)
    {
        if (draft.Lines.Any(l => context.Accounts.TryGetValue(l.AccountId, out var account) && account.IsControl))
        {
            scope.RequireAdmin();
        }
    }

    /// <summary>
    /// Splits a manual journal line that carries a tax code: the entered amount is gross, the account gets the net,
    /// a tax line goes to the code's tax account, and the net line keeps the code/amount/direction for reporting.
    /// </summary>
    private async Task<JournalDraft> ExpandTaxAsync(JournalDraft draft, Ledger ledger, CancellationToken ct)
    {
        if (draft.Lines.All(l => l.TaxCodeId is null))
        {
            return draft;
        }
        var codeIds = draft.Lines.Where(l => l.TaxCodeId != null).Select(l => l.TaxCodeId!.Value).Distinct().ToList();
        var codes = await db.TaxCodes.AsNoTracking().Where(t => t.LedgerId == ledger.Id && codeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var taxAccountIds = codes.Values.Where(c => c.AccountId != null).Select(c => c.AccountId!.Value).Distinct().ToList();
        var accountIds = draft.Lines.Select(l => l.AccountId).Concat(taxAccountIds).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => a.LedgerId == ledger.Id && accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var taxAccounts = (await db.AccountUsages.AsNoTracking()
            .Where(u => u.LedgerId == ledger.Id && taxAccountIds.Contains(u.AccountId)
                && (u.Usage == Usage.OutputVat || u.Usage == Usage.InputVat || u.Usage == Usage.NonDeductibleVat
                    || u.Usage == Usage.WithholdingPayable || u.Usage == Usage.OtherTaxPayable))
            .Select(u => u.AccountId).ToListAsync(ct)).ToHashSet();
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var lines = new List<DraftLine>();
        foreach (var line in draft.Lines)
        {
            if (line.TaxCodeId is not { } codeId)
            {
                if (line.TaxDirection is not null)
                {
                    throw BooksProblem.Invalid("tax_direction needs a tax_code_id.");
                }
                lines.Add(line);
                continue;
            }
            if (!codes.TryGetValue(codeId, out var code))
            {
                throw BooksProblem.Invalid("tax_code_id must be a tax code of this ledger.");
            }
            if (code.Kind is not (TaxKind.VatOutput or TaxKind.VatInput or TaxKind.VatInputNonDeductible or TaxKind.OtherTax))
            {
                throw BooksProblem.Invalid($"{code.Name} cannot be used on a manual journal; choose a VAT or other tax code.");
            }
            if (!code.AppliesOn(draft.AccountingDate))
            {
                throw BooksProblem.Invalid($"{code.Name} does not apply on {draft.AccountingDate:yyyy-MM-dd}.");
            }
            if (code.AccountId is not { } taxAccountId || !accounts.TryGetValue(taxAccountId, out var taxAccount) || !taxAccount.Active)
            {
                throw BooksProblem.Invalid($"{code.Name} has no active tax account.");
            }
            if (!accounts.TryGetValue(line.AccountId, out var lineAccount))
            {
                throw BooksProblem.Invalid("The line's account is not in this ledger.");
            }
            if (lineAccount.IsControl || lineAccount.IsBank || taxAccounts.Contains(line.AccountId))
            {
                throw BooksProblem.Invalid("Tax codes apply to income, expense or other tax-bearing accounts, not to control, bank or tax accounts.");
            }
            var direction = line.TaxDirection ?? (code.Kind == TaxKind.VatOutput || code.Kind == TaxKind.OtherTax ? TaxDirection.Sale : TaxDirection.Purchase);
            var allowed = code.Kind == TaxKind.VatOutput || code.Kind == TaxKind.OtherTax
                ? new[] { TaxDirection.Sale, TaxDirection.SaleAdjustment }
                : new[] { TaxDirection.Purchase, TaxDirection.PurchaseAdjustment };
            if (!allowed.Contains(direction))
            {
                throw BooksProblem.Invalid($"{code.Name} is a {(code.Kind == TaxKind.VatOutput || code.Kind == TaxKind.OtherTax ? "sale" : "purchase")} tax code.");
            }
            var tax = TaxCalculator.ForGross(line.Amount, code.Rate, minor);
            var net = line.Amount - tax.Amount;
            if (net == 0)
            {
                throw BooksProblem.Invalid("The gross amount leaves no net amount after tax.");
            }
            lines.Add(line with
            {
                Amount = net,
                TaxCodeId = code.Id,
                TaxAmount = tax.Amount,
                TaxCalculated = tax.Calculated,
                TaxDirection = direction,
            });
            if (tax.Amount != 0)
            {
                lines.Add(new DraftLine(taxAccountId, tax.Amount, $"{code.Name} (tax)", TaxCodeId: code.Id,
                    TaxAmount: tax.Amount, TaxCalculated: tax.Calculated, TaxDirection: direction, TaxLine: true));
            }
        }
        return draft with { Lines = lines };
    }

    internal JournalRevision AddRevision(Journal journal, JournalDraft draft, RevisionStatus status, IReadOnlyList<JournalIssue> issues, Guid userId, DateTimeOffset now)
    {
        var revision = new JournalRevision(
            Guid.CreateVersion7(), journal, journal.RevisionCount + 1, JournalKind.Manual, status, draft.AccountingDate,
            Trim(draft.Reference), Trim(draft.Description), issues.Count == 0 ? null : BooksJson.Serialize(issues), userId, now);
        db.JournalRevisions.Add(revision);
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var line = draft.Lines[i];
            db.JournalLines.Add(new JournalLine(revision, i + 1, line.LineId ?? Guid.CreateVersion7(), line.AccountId, Trim(line.Description), line.Amount, line.PartyId, line.ItemId, line.Quantity, line.CurrencyAmount, line.ClassId, line.LocationId,
                line.TaxCodeId, line.TaxAmount, line.TaxCalculated, line.TaxDirection, line.TaxLine));
        }
        journal.Apply(revision);
        return revision;
    }

    internal async Task<JournalContext> ContextAsync(Ledger ledger, JournalDraft draft, DateOnly? previousDate, CancellationToken ct)
    {
        var accountIds = draft.Lines.Select(l => l.AccountId).Distinct().ToList();
        var foreign = await db.BankAccounts.AsNoTracking()
            .Where(b => b.LedgerId == ledger.Id && accountIds.Contains(b.AccountId) && b.Currency != ledger.BaseCurrency)
            .ToDictionaryAsync(b => b.AccountId, b => b.Currency, ct);
        var accounts = (await db.Accounts.AsNoTracking().Where(a => a.LedgerId == ledger.Id && accountIds.Contains(a.Id)).ToListAsync(ct))
            .ToDictionary(a => a.Id, a => foreign.TryGetValue(a.Id, out var currency)
                ? new AccountFacts(a.Id, a.Active, a.IsControl, currency, Currencies.Require(currency).MinorUnits)
                : new AccountFacts(a.Id, a.Active, a.IsControl));
        var partyIds = draft.Lines.Where(l => l.PartyId != null).Select(l => l.PartyId!.Value).Distinct().ToList();
        var parties = await db.Parties.AsNoTracking()
            .Where(p => p.LedgerId == ledger.Id && partyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Active, ct);
        var itemIds = draft.Lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).Distinct().ToList();
        var items = await db.Items.AsNoTracking()
            .Where(i => i.LedgerId == ledger.Id && itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Active, ct);
        var classIds = draft.Lines.Where(l => l.ClassId != null).Select(l => l.ClassId!.Value).Distinct().ToList();
        var classes = await db.Classes.AsNoTracking()
            .Where(c => c.LedgerId == ledger.Id && classIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Active, ct);
        var locationIds = draft.Lines.Where(l => l.LocationId != null).Select(l => l.LocationId!.Value).Distinct().ToList();
        var locations = await db.Locations.AsNoTracking()
            .Where(l => l.LedgerId == ledger.Id && locationIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Active, ct);
        return new JournalContext(Currencies.Require(ledger.BaseCurrency).MinorUnits, ledger.ClosedThrough, accounts, previousDate, parties, Items: items,
            Classes: classes, Locations: locations);
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
            lines.Select(l => new JournalLineDto(l.LineId, l.AccountId, l.PartyId, l.ItemId, l.Quantity?.ToString("0.####", CultureInfo.InvariantCulture), Format(Math.Max(l.Amount, 0), minor), Format(Math.Max(-l.Amount, 0), minor), l.Description,
                l.CurrencyAmount?.ToString("0.####", CultureInfo.InvariantCulture), l.ClassId, l.LocationId, l.TaxCodeId,
                l.TaxAmount is { } tax ? Format(tax, minor) : null, l.TaxCalculated is { } calculated ? Format(calculated, minor) : null,
                l.TaxDirection is { } direction ? Text.Of(direction) : null, l.TaxLine)).ToList(),
            issues, revision.RevisionNo, journal.CreatedBy, journal.CreatedAt, journal.Version.ToString());
    }

    private static string Format(decimal amount, int minorUnits) => amount.ToString("F" + minorUnits, CultureInfo.InvariantCulture);

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
