using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reporting;

public sealed record TrialBalanceRow(
    Guid? AccountId, string? Code, string Name, string Type, Guid? GroupId,
    string OpeningDebit, string OpeningCredit, string PeriodDebit, string PeriodCredit, string ClosingDebit, string ClosingCredit);

public sealed record TrialBalanceTotals(string OpeningDebit, string OpeningCredit, string PeriodDebit, string PeriodCredit, string ClosingDebit, string ClosingCredit);

public sealed record ReportWarning(string Code, string Message);

public sealed record TrialBalance(
    DateOnly From, DateOnly To, string Currency, IReadOnlyList<TrialBalanceRow> Rows, TrialBalanceTotals Totals, bool Balanced,
    IReadOnlyList<ReportWarning> Warnings);

public sealed record GlLine(DateOnly Date, Guid JournalId, long JournalNo, string? Reference, string? Description, string Debit, string Credit, string Balance);

public sealed record GlDetail(
    DateOnly From, DateOnly To, string Currency, Guid AccountId, string Code, string Name,
    string OpeningBalance, IReadOnlyList<GlLine> Lines, string PeriodDebit, string PeriodCredit, string ClosingBalance);

/// <summary>
/// Trial Balance and GL Detail over effective postings only (rule 37): drafts, errors, superseded and deleted
/// revisions never count. Income and Cost &amp; Expense accounts start each fiscal year at zero; earlier profit is shown
/// as a calculated Retained Earnings line, never posted (rule 20, no closing journals).
/// </summary>
public sealed class ReportService(IBooksDb db, ReadRunner reads)
{
    public const string RetainedEarningsLine = "Retained earnings (calculated)";

    private sealed record Movement(Guid AccountId, decimal Opening, decimal PriorProfit, decimal PeriodDebit, decimal PeriodCredit);

    private sealed record Total(decimal Amount);

    private sealed record Posting(DateOnly AccountingDate, Guid JournalId, long JournalNo, string? Reference, string? Description, decimal Amount);

    public Task<TrialBalance> TrialBalanceAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly from, DateOnly to, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            var ledger = await LedgerForReportAsync(scope, ledgerId, from, to, ct);
            var fyStart = FiscalYearStart(from, ledger.FyStartMonth);
            var movements = await db.Database.SqlQuery<Movement>($"""
                SELECT a.id AS account_id,
                    COALESCE(SUM(l.amount) FILTER (WHERE r.accounting_date < {from}
                        AND (a.type IN ('assets', 'liabilities', 'equity') OR r.accounting_date >= {fyStart})), 0) AS opening,
                    COALESCE(SUM(l.amount) FILTER (WHERE r.accounting_date < {fyStart}
                        AND a.type IN ('income', 'cost_and_expense')), 0) AS prior_profit,
                    COALESCE(SUM(l.amount) FILTER (WHERE l.amount > 0 AND r.accounting_date >= {from}), 0) AS period_debit,
                    COALESCE(-SUM(l.amount) FILTER (WHERE l.amount < 0 AND r.accounting_date >= {from}), 0) AS period_credit
                FROM journals j
                JOIN journal_revisions r ON r.id = j.effective_revision_id
                JOIN journal_lines l ON l.revision_id = r.id
                JOIN accounts a ON a.id = l.account_id
                WHERE j.ledger_id = {ledgerId} AND r.accounting_date <= {to}
                GROUP BY a.id
                """).ToListAsync(ct);
            var accounts = await db.Accounts.AsNoTracking().Where(a => a.LedgerId == ledgerId).ToDictionaryAsync(a => a.Id, ct);
            var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;

            var rows = movements
                .Select(m => (Account: accounts[m.AccountId], m.Opening, m.PeriodDebit, m.PeriodCredit))
                .Where(x => x.Opening != 0 || x.PeriodDebit != 0 || x.PeriodCredit != 0)
                .OrderBy(x => x.Account.Code, StringComparer.OrdinalIgnoreCase)
                .Select(x => Row(x.Account.Id, x.Account.Code, x.Account.Name, Text.Of(x.Account.Type), x.Account.GroupId, x.Opening, x.PeriodDebit, x.PeriodCredit, minor))
                .ToList();
            var priorProfit = movements.Sum(m => m.PriorProfit);
            if (priorProfit != 0)
            {
                rows.Add(Row(null, null, RetainedEarningsLine, Text.Of(AccountType.Equity), null, priorProfit, 0, 0, minor));
            }

            var opening = movements.Sum(m => m.Opening) + priorProfit;
            var period = (Debit: movements.Sum(m => m.PeriodDebit), Credit: movements.Sum(m => m.PeriodCredit));
            var openingSides = rows.Aggregate((D: 0m, C: 0m), (s, r) => (s.D + Parse(r.OpeningDebit), s.C + Parse(r.OpeningCredit)));
            var closingSides = rows.Aggregate((D: 0m, C: 0m), (s, r) => (s.D + Parse(r.ClosingDebit), s.C + Parse(r.ClosingCredit)));
            var totals = new TrialBalanceTotals(
                Format(openingSides.D, minor), Format(openingSides.C, minor), Format(period.Debit, minor), Format(period.Credit, minor),
                Format(closingSides.D, minor), Format(closingSides.C, minor));
            var balanced = opening == 0 && period.Debit == period.Credit && closingSides.D == closingSides.C;
            var warnings = new List<ReportWarning>();
            var purposes = await db.AccountPurposes.AsNoTracking().Where(p => p.LedgerId == ledgerId).ToDictionaryAsync(p => p.Purpose, p => p.AccountId, ct);
            decimal Closing(SystemPurpose purpose) =>
                purposes.TryGetValue(purpose, out var id) && movements.SingleOrDefault(m => m.AccountId == id) is { } m
                    ? m.Opening + m.PeriodDebit - m.PeriodCredit
                    : 0;
            if (Closing(SystemPurpose.OpeningBalanceClearing) is var clearing && clearing != 0)
            {
                warnings.Add(new ReportWarning("OPENING_BALANCE_CLEARING_NOT_ZERO",
                    $"Opening Balance Clearing should be zero after setup; it is {Format(Math.Abs(clearing), minor)} {(clearing > 0 ? "Dr" : "Cr")}."));
            }
            if (Closing(SystemPurpose.Suspense) is var suspense && suspense != 0)
            {
                warnings.Add(new ReportWarning("SUSPENSE_NOT_ZERO",
                    $"Suspense holds {Format(Math.Abs(suspense), minor)} {(suspense > 0 ? "Dr" : "Cr")} that still needs to be classified."));
            }
            return new TrialBalance(from, to, ledger.BaseCurrency, rows, totals, balanced, warnings);
        }, ct);

    public Task<GlDetail> GlDetailAsync(Principal principal, Guid orgId, Guid ledgerId, Guid accountId, DateOnly from, DateOnly to, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            var ledger = await LedgerForReportAsync(scope, ledgerId, from, to, ct);
            var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == accountId, ct)
                ?? throw BooksProblem.NotFound();
            var fyStart = FiscalYearStart(from, ledger.FyStartMonth);
            var balanceStart = account.Type is AccountType.Income or AccountType.CostAndExpense ? fyStart : DateOnly.MinValue;
            var opening = (await db.Database.SqlQuery<Total>($"""
                SELECT COALESCE(SUM(l.amount), 0) AS amount
                FROM journals j
                JOIN journal_revisions r ON r.id = j.effective_revision_id
                JOIN journal_lines l ON l.revision_id = r.id
                WHERE j.ledger_id = {ledgerId} AND l.account_id = {accountId}
                    AND r.accounting_date >= {balanceStart} AND r.accounting_date < {from}
                """).SingleAsync(ct)).Amount;
            var postings = await db.Database.SqlQuery<Posting>($"""
                SELECT r.accounting_date AS accounting_date, j.id AS journal_id, j.journal_no AS journal_no,
                    r.reference AS reference, COALESCE(l.description, r.description) AS description, l.amount AS amount
                FROM journals j
                JOIN journal_revisions r ON r.id = j.effective_revision_id
                JOIN journal_lines l ON l.revision_id = r.id
                WHERE j.ledger_id = {ledgerId} AND l.account_id = {accountId}
                    AND r.accounting_date BETWEEN {from} AND {to}
                ORDER BY r.accounting_date, j.journal_no, l.line_no
                """).ToListAsync(ct);

            var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
            var running = opening;
            var lines = postings.Select(p =>
            {
                running += p.Amount;
                return new GlLine(p.AccountingDate, p.JournalId, p.JournalNo, p.Reference, p.Description,
                    Format(Math.Max(p.Amount, 0), minor), Format(Math.Max(-p.Amount, 0), minor), Format(running, minor));
            }).ToList();
            return new GlDetail(from, to, ledger.BaseCurrency, account.Id, account.Code, account.Name, Format(opening, minor), lines,
                Format(postings.Where(p => p.Amount > 0).Sum(p => p.Amount), minor),
                Format(-postings.Where(p => p.Amount < 0).Sum(p => p.Amount), minor),
                Format(running, minor));
        }, ct);

    /// <summary>The first day of the fiscal year that contains the date.</summary>
    public static DateOnly FiscalYearStart(DateOnly date, int startMonth) =>
        new(date.Month >= startMonth ? date.Year : date.Year - 1, startMonth, 1);

    private async Task<Ledger> LedgerForReportAsync(OrgScope scope, Guid ledgerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
        if (from > to)
        {
            throw BooksProblem.Invalid("from must be on or before to.");
        }
        return await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
    }

    /// <summary>A balance is shown on its own side: debit when positive, credit when negative.</summary>
    private static TrialBalanceRow Row(Guid? id, string? code, string name, string type, Guid? groupId, decimal opening, decimal periodDebit, decimal periodCredit, int minor)
    {
        var closing = opening + periodDebit - periodCredit;
        return new TrialBalanceRow(id, code, name, type, groupId,
            Format(Math.Max(opening, 0), minor), Format(Math.Max(-opening, 0), minor),
            Format(periodDebit, minor), Format(periodCredit, minor),
            Format(Math.Max(closing, 0), minor), Format(Math.Max(-closing, 0), minor));
    }

    private static string Format(decimal amount, int minor) => amount.ToString("F" + minor, CultureInfo.InvariantCulture);

    private static decimal Parse(string amount) => decimal.Parse(amount, CultureInfo.InvariantCulture);
}
