using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record BankPostingDto(Guid JournalId, long JournalNo, DateOnly Date, string? Reference, string? Description, string Source, string Amount, DateOnly? ClearedOn);

/// <param name="ClearedBalance">The book balance of postings cleared on or before the date: what the statement should show.</param>
/// <param name="Difference">Statement balance less cleared balance; zero when the account reconciles.</param>
/// <param name="Pending">Postings up to the date not yet cleared (cheques in transit, deposits not yet credited).</param>
public sealed record BankReconciliationDto(
    Guid AccountId, string Code, string Name, DateOnly AsOf, string Currency, string BookBalance, string ClearedBalance,
    string? StatementBalance, string? Difference, bool? Reconciled, IReadOnlyList<BankPostingDto> Pending);

/// <summary>
/// Bank reconciliation (owner decision, Manager.io style): postings on a bank account are pending or cleared, and
/// the statement balance is compared with the cleared balance. Clearing changes no posting, so AI clients may clear;
/// a cleared posting must be uncleared before what posted it changes (P9).
/// </summary>
public sealed class BankReconciliationService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    private sealed record Posting(Guid JournalId, long JournalNo, DateOnly AccountingDate, string? Reference, string? Description, string? SourceKind, decimal Amount);

    public Task<BankReconciliationDto> ReconciliationAsync(Principal principal, Guid orgId, Guid ledgerId, Guid accountId, DateOnly asOf, decimal? statementBalance, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var account = await BankAccountAsync(ledgerId, accountId, ct);
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            var minor = Currencies.Require(currency).MinorUnits;
            string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
            var postings = await PostingsAsync(ledgerId, accountId, asOf, ct);
            var cleared = await db.BankClearings.AsNoTracking().Where(c => c.AccountId == accountId).ToDictionaryAsync(c => c.JournalId, c => c.ClearedOn, ct);
            bool IsCleared(Posting p) => cleared.TryGetValue(p.JournalId, out var on) && on <= asOf;
            var book = postings.Sum(p => p.Amount);
            var clearedBalance = postings.Where(IsCleared).Sum(p => p.Amount);
            var pending = postings.Where(p => !IsCleared(p))
                .Select(p => new BankPostingDto(p.JournalId, p.JournalNo, p.AccountingDate, p.Reference, p.Description, p.SourceKind ?? "journal", F(p.Amount),
                    cleared.TryGetValue(p.JournalId, out var later) ? later : null))
                .ToList();
            return new BankReconciliationDto(account.Id, account.Code, account.Name, asOf, currency, F(book), F(clearedBalance),
                statementBalance is { } s ? F(s) : null, statementBalance is { } d ? F(d - clearedBalance) : null, statementBalance is { } r ? r == clearedBalance : null, pending);
        }, ct);

    /// <summary>Marks postings cleared on a statement date; clearing again moves the date.</summary>
    public Task<WriteResult> ClearAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId, DateOnly clearedOn, IReadOnlyList<Guid> journalIds, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("bank.clear", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var account = await BankAccountAsync(ledgerId, accountId, ct);
            var postings = (await PostingsAsync(ledgerId, accountId, DateOnly.MaxValue, ct)).ToDictionary(p => p.JournalId);
            var errors = journalIds.Distinct().Where(id => !postings.ContainsKey(id)).ToList();
            if (errors.Count > 0)
            {
                throw BooksProblem.Invalid($"These journals have no posting on {account.Code}: {string.Join(", ", errors)}.");
            }
            var early = journalIds.Distinct().Where(id => postings[id].AccountingDate > clearedOn).ToList();
            if (early.Count > 0)
            {
                throw BooksProblem.Invalid($"A posting cannot clear before its date; journal {string.Join(", ", early.Select(id => postings[id].JournalNo))}.");
            }
            var existing = await db.BankClearings.Where(c => c.AccountId == accountId && journalIds.Contains(c.JournalId)).ToDictionaryAsync(c => c.JournalId, ct);
            foreach (var id in journalIds.Distinct())
            {
                if (existing.TryGetValue(id, out var clearing))
                {
                    clearing.Reclear(clearedOn, principal.UserId, scope.Now);
                }
                else
                {
                    db.BankClearings.Add(new BankClearing(orgId, ledgerId, accountId, id, clearedOn, principal.UserId, scope.Now));
                }
            }
            scope.Audit("bank.cleared", "account", accountId, new { cleared_on = clearedOn, journal_ids = journalIds });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, new { account_id = accountId, cleared_on = clearedOn, journal_ids = journalIds.Distinct() });
        }, ct);

    public Task<WriteResult> UnclearAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId, IReadOnlyList<Guid> journalIds, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("bank.unclear", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            await BankAccountAsync(ledgerId, accountId, ct);
            var removed = await db.BankClearings.Where(c => c.AccountId == accountId && journalIds.Contains(c.JournalId)).ToListAsync(ct);
            db.BankClearings.RemoveRange(removed);
            scope.Audit("bank.uncleared", "account", accountId, new { journal_ids = removed.Select(c => c.JournalId), cleared_on = removed.Select(c => c.ClearedOn) });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, new { account_id = accountId, journal_ids = removed.Select(c => c.JournalId) });
        }, ct);

    /// <summary>Refuses a change to a journal whose bank posting is cleared (P9: unclear it first, which is audited).</summary>
    public static async Task EnsureNotClearedAsync(IBooksDb db, Guid journalId, CancellationToken ct)
    {
        if (await db.BankClearings.AnyAsync(c => c.JournalId == journalId, ct))
        {
            throw BooksProblem.Conflict("BANK_CLEARED", "This posting is cleared on a bank reconciliation; unclear it first.");
        }
    }

    private async Task<Account> BankAccountAsync(Guid ledgerId, Guid accountId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == accountId, ct) ?? throw BooksProblem.NotFound();
        return await db.AccountUsages.AnyAsync(u => u.AccountId == accountId && u.Usage == Usage.Bank, ct)
            ? account
            : throw BooksProblem.Invalid("The account has no Bank usage.");
    }

    /// <summary>Effective postings on the account up to the date, one per journal (summed when a journal hits it twice).</summary>
    private async Task<List<Posting>> PostingsAsync(Guid ledgerId, Guid accountId, DateOnly asOf, CancellationToken ct) =>
        await db.Database.SqlQuery<Posting>($"""
            SELECT j.id AS journal_id, j.journal_no AS journal_no, r.accounting_date AS accounting_date, r.reference AS reference,
                   r.description AS description, j.source_kind AS source_kind, SUM(l.amount) AS amount
            FROM journals j
            JOIN journal_revisions r ON r.id = j.effective_revision_id
            JOIN journal_lines l ON l.revision_id = r.id AND l.account_id = {accountId}
            WHERE j.ledger_id = {ledgerId} AND r.accounting_date <= {asOf}
            GROUP BY j.id, j.journal_no, r.accounting_date, r.reference, r.description, j.source_kind
            ORDER BY r.accounting_date, j.journal_no
            """).ToListAsync(ct);
}
