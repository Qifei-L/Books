using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reporting;

/// <param name="Kind">group, account, or calculated (a line computed by the report, never posted).</param>
/// <param name="Amount">Shown in the section's natural sign: debit-positive for Assets and Cost &amp; Expense,
/// credit-positive for Liabilities, Equity and Income. A contra balance shows as negative (rule 14).</param>
public sealed record StatementLine(string Kind, Guid? Id, string? Code, string Name, string Amount, IReadOnlyList<StatementLine> Children);

public sealed record StatementSection(string Type, string Title, IReadOnlyList<StatementLine> Lines, string Total);

public sealed record ProfitAndLoss(DateOnly From, DateOnly To, string Currency, IReadOnlyList<StatementSection> Sections, string NetProfit);

public sealed record BalanceSheet(
    DateOnly AsOf, string Currency, IReadOnlyList<StatementSection> Sections,
    string TotalAssets, string TotalLiabilitiesAndEquity, bool Balanced);

/// <summary>
/// Balance Sheet and P&amp;L from effective postings only, in one snapshot (rules 37, 42). Year-end needs no closing
/// journals: prior years' profit and current year profit are calculated lines, never posted, so they cannot be
/// double-counted with the Retained Earnings account (rule 40). All accounts show, including zero balances; every
/// group level shows its subtotal (owner decisions 2026-10-23).
/// </summary>
public sealed class StatementService(IBooksDb db, ReadRunner reads)
{
    public const string PriorYearsProfit = "Retained earnings, prior years (calculated)";
    public const string CurrentYearProfit = "Current year profit (calculated)";

    private sealed record Sum(Guid AccountId, decimal Amount);

    public Task<ProfitAndLoss> ProfitAndLossAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly from, DateOnly to, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            var ledger = await LedgerAsync(scope, ledgerId, from, to, ct);
            var chart = await Chart.LoadAsync(db, ledgerId, ct);
            var sums = await SumsAsync(ledgerId, from, to, ct);
            var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
            var sections = new[] { AccountType.Income, AccountType.CostAndExpense }
                .Select(type => chart.Section(type, sums, minor, [])).ToList();
            // Net profit = Income − Cost & Expense = −(sum of signed P&L postings).
            var net = -chart.Accounts.Values.Where(a => a.Type is AccountType.Income or AccountType.CostAndExpense)
                .Sum(a => sums.GetValueOrDefault(a.Id));
            return new ProfitAndLoss(from, to, ledger.BaseCurrency, sections, Format(net, minor));
        }, ct);

    public Task<BalanceSheet> BalanceSheetAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly asOf, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            var ledger = await LedgerAsync(scope, ledgerId, DateOnly.MinValue, asOf, ct);
            var (chart, sums) = await FoldRevaluationAsync(ledgerId, await Chart.LoadAsync(db, ledgerId, ct), await SumsAsync(ledgerId, DateOnly.MinValue, asOf, ct), ct);
            var fyStart = ReportService.FiscalYearStart(asOf, ledger.FyStartMonth);
            var currentYear = await SumsAsync(ledgerId, fyStart, asOf, ct);
            var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;

            decimal ProfitIn(IReadOnlyDictionary<Guid, decimal> source) =>
                -chart.Accounts.Values.Where(a => a.Type is AccountType.Income or AccountType.CostAndExpense).Sum(a => source.GetValueOrDefault(a.Id));
            var currentProfit = ProfitIn(currentYear);
            var priorProfit = ProfitIn(sums) - currentProfit;
            var calculated = new List<StatementLine>
            {
                new("calculated", null, null, PriorYearsProfit, Format(priorProfit, minor), []),
                new("calculated", null, null, CurrentYearProfit, Format(currentProfit, minor), []),
            };

            var sections = new List<StatementSection>
            {
                chart.Section(AccountType.Assets, sums, minor, []),
                chart.Section(AccountType.Liabilities, sums, minor, []),
                chart.Section(AccountType.Equity, sums, minor, calculated),
            };
            var assets = Parse(sections[0].Total);
            var liabilitiesAndEquity = Parse(sections[1].Total) + Parse(sections[2].Total);
            return new BalanceSheet(asOf, ledger.BaseCurrency, sections, Format(assets, minor), Format(liabilitiesAndEquity, minor), assets == liabilitiesAndEquity);
        }, ct);

    /// <summary>
    /// Owner decision (M12.2b): receivables and payables show as one line each. The exchange revaluation accounts
    /// beside them are added into the ledger's Receivable and Payable account when it has exactly one, and not shown.
    /// </summary>
    private async Task<(Chart, IReadOnlyDictionary<Guid, decimal>)> FoldRevaluationAsync(Guid ledgerId, Chart chart, IReadOnlyDictionary<Guid, decimal> sums, CancellationToken ct)
    {
        var usages = await db.AccountUsages.AsNoTracking().Where(u => u.LedgerId == ledgerId
                && (u.Usage == Usage.Receivable || u.Usage == Usage.Payable || u.Usage == Usage.ReceivableRevaluation || u.Usage == Usage.PayableRevaluation))
            .Select(u => new { u.AccountId, u.Usage }).ToListAsync(ct);
        var folded = sums.ToDictionary(s => s.Key, s => s.Value);
        var hidden = new HashSet<Guid>();
        foreach (var (revaluation, control) in new[] { (Usage.ReceivableRevaluation, Usage.Receivable), (Usage.PayableRevaluation, Usage.Payable) })
        {
            var targets = usages.Where(u => u.Usage == control).Select(u => u.AccountId).ToList();
            if (targets.Count != 1)
            {
                continue;
            }
            foreach (var account in usages.Where(u => u.Usage == revaluation).Select(u => u.AccountId))
            {
                folded[targets[0]] = folded.GetValueOrDefault(targets[0]) + folded.GetValueOrDefault(account);
                folded.Remove(account);
                hidden.Add(account);
            }
        }
        return (chart.Without(hidden), folded);
    }

    private async Task<Ledger> LedgerAsync(OrgScope scope, Guid ledgerId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
        if (from > to)
        {
            throw BooksProblem.Invalid("from must be on or before to.");
        }
        return await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
    }

    /// <summary>Signed sum (debit positive) of effective postings per account, dates inclusive.</summary>
    private async Task<IReadOnlyDictionary<Guid, decimal>> SumsAsync(Guid ledgerId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await db.Database.SqlQuery<Sum>($"""
            SELECT l.account_id AS account_id, SUM(l.amount) AS amount
            FROM journals j
            JOIN journal_revisions r ON r.id = j.effective_revision_id
            JOIN journal_lines l ON l.revision_id = r.id
            WHERE j.ledger_id = {ledgerId} AND r.accounting_date BETWEEN {from} AND {to}
            GROUP BY l.account_id
            """).ToListAsync(ct)).ToDictionary(s => s.AccountId, s => s.Amount);

    internal static string Format(decimal amount, int minor) => amount.ToString("F" + minor, CultureInfo.InvariantCulture);

    private static decimal Parse(string amount) => decimal.Parse(amount, CultureInfo.InvariantCulture);

    /// <summary>The chart as a tree: groups (nested within a type) holding accounts, plus ungrouped accounts.</summary>
    private sealed class Chart(Dictionary<Guid, Account> accounts, List<AccountGroup> groups)
    {
        public Dictionary<Guid, Account> Accounts => accounts;

        public Chart Without(IReadOnlySet<Guid> hidden) =>
            hidden.Count == 0 ? this : new(accounts.Where(a => !hidden.Contains(a.Key)).ToDictionary(a => a.Key, a => a.Value), groups);

        public static async Task<Chart> LoadAsync(IBooksDb db, Guid ledgerId, CancellationToken ct) => new(
            await db.Accounts.AsNoTracking().Where(a => a.LedgerId == ledgerId).ToDictionaryAsync(a => a.Id, ct),
            await db.AccountGroups.AsNoTracking().Where(g => g.LedgerId == ledgerId).ToListAsync(ct));

        public StatementSection Section(AccountType type, IReadOnlyDictionary<Guid, decimal> sums, int minor, IReadOnlyList<StatementLine> extra)
        {
            // Debit-positive types show sums as they are; credit-positive types show them negated.
            var sign = type is AccountType.Assets or AccountType.CostAndExpense ? 1 : -1;
            (StatementLine Line, decimal Amount) AccountLine(Account a)
            {
                var amount = sign * sums.GetValueOrDefault(a.Id);
                return (new StatementLine("account", a.Id, a.Code, a.Name, Format(amount, minor), []), amount);
            }
            (StatementLine Line, decimal Amount) GroupLine(AccountGroup g)
            {
                var children = groups.Where(c => c.ParentId == g.Id).OrderBy(c => c.Name).Select(GroupLine)
                    .Concat(accounts.Values.Where(a => a.GroupId == g.Id).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).Select(AccountLine))
                    .ToList();
                var subtotal = children.Sum(c => c.Amount);
                return (new StatementLine("group", g.Id, null, g.Name, Format(subtotal, minor), children.Select(c => c.Line).ToList()), subtotal);
            }

            var lines = groups.Where(g => g.Type == type && g.ParentId == null).OrderBy(g => g.Name).Select(GroupLine)
                .Concat(accounts.Values.Where(a => a.Type == type && a.GroupId == null).OrderBy(a => a.Code, StringComparer.OrdinalIgnoreCase).Select(AccountLine))
                .ToList();
            var total = lines.Sum(l => l.Amount) + extra.Sum(e => Parse(e.Amount));
            return new StatementSection(Text.Of(type), Title(type), lines.Select(l => l.Line).Concat(extra).ToList(), Format(total, minor));
        }

        private static string Title(AccountType type) => type switch
        {
            AccountType.Assets => "Assets",
            AccountType.Liabilities => "Liabilities",
            AccountType.Equity => "Equity",
            AccountType.Income => "Income",
            _ => "Cost & Expense",
        };
    }
}
