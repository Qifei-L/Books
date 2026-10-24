using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <param name="Amount">Positive for money in, negative for money out.</param>
/// <param name="ExternalId">The bank's transaction ID, if the statement shows one.</param>
public sealed record StatementLineInput(DateOnly Date, string? Description, string? Reference, decimal Amount, string? ExternalId = null);

public sealed record StatementLineDto(int LineNo, DateOnly Date, string? Description, string? Reference, string Amount, string Status, Guid? JournalId, Guid? PaymentId);

public sealed record BankStatementDto(
    Guid Id, Guid AccountId, DateOnly From, DateOnly To, string OpeningBalance, string ClosingBalance,
    int Matched, int Drafted, int Duplicates, IReadOnlyList<StatementLineDto> Lines);

/// <summary>
/// Bank statements imported by AI through MCP (owner decisions): the lines must add up from the opening to the
/// closing balance; lines seen before are skipped; a line matching a pending posting (same amount, within five
/// days) clears it; any other line becomes a draft payment or receipt to Suspense, which a person posts.
/// </summary>
public sealed class BankStatementService(IBooksDb db, WriteRunner writes, ReadRunner reads, PaymentService payments)
{
    private sealed record Pending(Guid JournalId, long JournalNo, DateOnly AccountingDate, decimal Amount);

    private const int MatchDays = 5;

    public Task<IReadOnlyList<BankStatementDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, Guid accountId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<BankStatementDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var minor = await MinorAsync(ledgerId, accountId, ct);
            return (await db.BankStatements.AsNoTracking().Include(s => s.Lines).Where(s => s.LedgerId == ledgerId && s.AccountId == accountId)
                .OrderByDescending(s => s.ToDate).ThenByDescending(s => s.ImportedAt).ToListAsync(ct)).Select(s => ToDto(s, minor)).ToList();
        }, ct);

    public Task<WriteResult> ImportAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId,
        decimal openingBalance, decimal closingBalance, IReadOnlyList<StatementLineInput> lines, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("bank_statement.import", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            if (!await db.AccountUsages.AnyAsync(u => u.LedgerId == ledgerId && u.AccountId == accountId && u.Usage == Usage.Bank, ct))
            {
                throw BooksProblem.Invalid("account_id must be an account with the Bank usage.");
            }
            var suspense = await db.AccountPurposes.Where(p => p.LedgerId == ledgerId && p.Purpose == SystemPurpose.Suspense).Select(p => (Guid?)p.AccountId).SingleOrDefaultAsync(ct)
                ?? throw BooksProblem.Conflict("NO_SUSPENSE_ACCOUNT", "Assign the Suspense purpose to an account first; unmatched statement lines go there.");
            BankStatement statement;
            try
            {
                statement = new BankStatement(Guid.CreateVersion7(), orgId, ledgerId, accountId, openingBalance, closingBalance,
                    lines.Select(l => new BankStatementLine(l.Date, l.Description, l.Reference, l.Amount, l.ExternalId)), principal.UserId, scope.Now);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }

            // Lines imported before: by the bank's transaction ID, or by date, amount and description.
            var earlier = await db.BankStatements.Where(s => s.AccountId == accountId).SelectMany(s => s.Lines)
                .Where(l => l.Status != StatementLineStatus.Duplicate)
                .Select(l => new { l.ExternalId, l.Date, l.Amount, l.Description }).ToListAsync(ct);
            var seenIds = earlier.Where(l => l.ExternalId != null).Select(l => l.ExternalId!).ToHashSet();
            var seen = earlier.Where(l => l.ExternalId == null).Select(l => (l.Date, l.Amount, l.Description)).ToList();

            var cleared = await db.BankClearings.Where(c => c.AccountId == accountId).Select(c => c.JournalId).ToListAsync(ct);
            var pending = (await db.Database.SqlQuery<Pending>($"""
                SELECT j.id AS journal_id, j.journal_no AS journal_no, r.accounting_date AS accounting_date, SUM(COALESCE(l.currency_amount, l.amount)) AS amount
                FROM journals j
                JOIN journal_revisions r ON r.id = j.effective_revision_id
                JOIN journal_lines l ON l.revision_id = r.id AND l.account_id = {accountId}
                WHERE j.ledger_id = {ledgerId}
                GROUP BY j.id, j.journal_no, r.accounting_date
                """).ToListAsync(ct)).Where(p => !cleared.Contains(p.JournalId)).ToList();

            foreach (var line in statement.Lines.OrderBy(l => l.Date).ThenBy(l => l.LineNo))
            {
                if (line.ExternalId is { } external ? !seenIds.Add(external) : seen.Remove((line.Date, line.Amount, line.Description)))
                {
                    line.Duplicate();
                    continue;
                }
                var match = pending.Where(p => p.Amount == line.Amount && Math.Abs(p.AccountingDate.DayNumber - line.Date.DayNumber) <= MatchDays)
                    .OrderBy(p => Math.Abs(p.AccountingDate.DayNumber - line.Date.DayNumber)).ThenBy(p => p.JournalNo).FirstOrDefault();
                if (match is not null)
                {
                    pending.Remove(match);
                    var on = line.Date >= match.AccountingDate ? line.Date : match.AccountingDate;
                    db.BankClearings.Add(new BankClearing(orgId, ledgerId, accountId, match.JournalId, on, principal.UserId, scope.Now));
                    line.Matched(match.JournalId);
                    continue;
                }
                var text = line.Description is null ? "Bank statement line" : $"Bank statement: {line.Description}";
                var (_, draft) = await payments.CreateOneAsync(scope, ledger,
                    new PaymentInput(accountId, null, line.Date, line.Reference ?? line.ExternalId, text, [new PaymentLineInput(null, suspense, line.Description, null, null, null, Math.Abs(line.Amount))]),
                    post: false, receipt: line.Amount > 0, ct);
                line.Drafted(draft.Id);
            }
            db.BankStatements.Add(statement);
            var dto = ToDto(statement, await MinorAsync(ledgerId, accountId, ct));
            scope.Audit("bank_statement.imported", "account", accountId, new { statement_id = statement.Id, dto.Matched, dto.Drafted, dto.Duplicates });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, dto);
        }, ct);

    /// <summary>
    /// Posts the statement's draft payments and receipts to Suspense and clears them on their statement dates. People
    /// only, like every posting; drafts already changed or deleted are left alone.
    /// </summary>
    public Task<WriteResult> PostDraftsAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid statementId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("bank_statement.post_drafts", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            JournalService.RefuseAgentPosting(principal, touchesPosting: true);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var statement = await db.BankStatements.Include(s => s.Lines).SingleOrDefaultAsync(s => s.LedgerId == ledgerId && s.Id == statementId, ct)
                ?? throw BooksProblem.NotFound();
            var posted = new List<int>();
            var notPosted = new List<object>();
            foreach (var line in statement.Lines.Where(l => l.Status == StatementLineStatus.Drafted))
            {
                var payment = await db.Payments.Include(p => p.Lines).SingleOrDefaultAsync(p => p.Id == line.PaymentId, ct);
                if (payment is not { Status: DocumentStatus.Draft })
                {
                    continue;
                }
                if (await payments.PostDraftAsync(scope, ledger, payment, ct) == "posted")
                {
                    db.BankClearings.Add(new BankClearing(orgId, ledgerId, statement.AccountId, payment.JournalId!.Value, line.Date, principal.UserId, scope.Now));
                    posted.Add(line.LineNo);
                }
                else
                {
                    notPosted.Add(new { line_no = line.LineNo, issues = payment.Issues });
                }
            }
            scope.Audit("bank_statement.drafts_posted", "account", statement.AccountId, new { statement_id = statementId, posted, not_posted = notPosted });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, new { statement_id = statementId, posted, not_posted = notPosted });
        }, ct);

    /// <summary>Statements are in the bank account's currency.</summary>
    private async Task<int> MinorAsync(Guid ledgerId, Guid accountId, CancellationToken ct) =>
        Currencies.Require(await db.BankAccounts.Where(b => b.AccountId == accountId).Select(b => b.Currency).SingleOrDefaultAsync(ct)
            ?? await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;

    private static BankStatementDto ToDto(BankStatement s, int minor)
    {
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        return new BankStatementDto(s.Id, s.AccountId, s.FromDate, s.ToDate, F(s.OpeningBalance), F(s.ClosingBalance),
            s.Lines.Count(l => l.Status == StatementLineStatus.Matched), s.Lines.Count(l => l.Status == StatementLineStatus.Drafted),
            s.Lines.Count(l => l.Status == StatementLineStatus.Duplicate),
            s.Lines.OrderBy(l => l.LineNo).Select(l => new StatementLineDto(l.LineNo, l.Date, l.Description, l.Reference, F(l.Amount), Text.Of(l.Status), l.JournalId, l.PaymentId)).ToList());
    }
}
