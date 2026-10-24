using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reporting;

public sealed record AgingRow(Guid SupplierId, string Supplier, string NotDue, string Days1To30, string Days31To60, string Days61To90, string Over90, string Total);

/// <param name="ControlBalance">Credit balance of all Payable accounts as of the date, from the journals.</param>
/// <param name="MatchesControl">Rule 43: the aging total equals the control balance.</param>
public sealed record ApAging(DateOnly AsOf, string Currency, IReadOnlyList<AgingRow> Rows, AgingRow Totals, string ControlBalance, bool MatchesControl);

public sealed record StatementItem(DateOnly Date, string Type, long Number, string? Reference, DateOnly DueDate, string Amount, string Balance);

public sealed record SupplierStatement(
    Guid SupplierId, string Supplier, DateOnly From, DateOnly To, string Currency, string OpeningBalance, IReadOnlyList<StatementItem> Items, string ClosingBalance);

/// <summary>
/// Accounts Payable reports, from posted purchase documents in one snapshot. An invoice is open for its payable
/// total; a debit note is a credit. Allocations of payments (rule 33) will reduce these amounts once payments exist.
/// </summary>
public sealed class PayablesReportService(IBooksDb db, ReadRunner reads)
{
    private sealed record Open(Guid SupplierId, DateOnly Date, DateOnly DueDate, string Type, long Number, string? Reference, decimal Amount);

    private sealed record Total(decimal Amount);

    public Task<ApAging> AgingAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly asOf, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var minor = await MinorAsync(ledgerId, ct);
            var items = await OpenItemsAsync(ledgerId, DateOnly.MinValue, asOf, ct);
            var names = await db.Parties.AsNoTracking().Where(p => p.LedgerId == ledgerId).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            decimal[] Buckets(IEnumerable<Open> open)
            {
                var b = new decimal[5];
                foreach (var item in open)
                {
                    var overdue = asOf.DayNumber - item.DueDate.DayNumber;
                    b[overdue <= 0 ? 0 : overdue <= 30 ? 1 : overdue <= 60 ? 2 : overdue <= 90 ? 3 : 4] += item.Amount;
                }
                return b;
            }
            AgingRow Row(Guid id, string name, decimal[] b) =>
                new(id, name, F(b[0], minor), F(b[1], minor), F(b[2], minor), F(b[3], minor), F(b[4], minor), F(b.Sum(), minor));

            var rows = items.GroupBy(i => i.SupplierId)
                .Select(g => (Id: g.Key, Name: names.GetValueOrDefault(g.Key, "?"), Buckets: Buckets(g)))
                .Where(r => r.Buckets.Sum() != 0)
                .OrderBy(r => r.Name)
                .Select(r => Row(r.Id, r.Name, r.Buckets))
                .ToList();
            var totals = Buckets(items);
            var control = -(await db.Database.SqlQuery<Total>($"""
                SELECT COALESCE(SUM(l.amount), 0) AS amount
                FROM journals j
                JOIN journal_revisions r ON r.id = j.effective_revision_id
                JOIN journal_lines l ON l.revision_id = r.id
                JOIN account_usages u ON u.account_id = l.account_id AND u.usage = 'payable'
                WHERE j.ledger_id = {ledgerId} AND r.accounting_date <= {asOf}
                """).SingleAsync(ct)).Amount;
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            return new ApAging(asOf, currency, rows, Row(Guid.Empty, "Total", totals), F(control, minor), control == totals.Sum());
        }, ct);

    public Task<SupplierStatement> StatementAsync(Principal principal, Guid orgId, Guid ledgerId, Guid supplierId, DateOnly from, DateOnly to, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            if (from > to)
            {
                throw BooksProblem.Invalid("from must be on or before to.");
            }
            var supplier = await db.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.LedgerId == ledgerId && p.Id == supplierId, ct) ?? throw BooksProblem.NotFound();
            var minor = await MinorAsync(ledgerId, ct);
            var all = (await OpenItemsAsync(ledgerId, DateOnly.MinValue, to, ct)).Where(i => i.SupplierId == supplierId).ToList();
            var opening = all.Where(i => i.Date < from).Sum(i => i.Amount);
            var running = opening;
            var items = all.Where(i => i.Date >= from).OrderBy(i => i.Date).ThenBy(i => i.Type).ThenBy(i => i.Number).Select(i =>
            {
                running += i.Amount;
                return new StatementItem(i.Date, i.Type, i.Number, i.Reference, i.DueDate, F(i.Amount, minor), F(running, minor));
            }).ToList();
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            return new SupplierStatement(supplier.Id, supplier.Name, from, to, currency, F(opening, minor), items, F(running, minor));
        }, ct);

    /// <summary>Posted invoices (owed, positive) and debit notes (credit, negative) dated within the range.</summary>
    private async Task<List<Open>> OpenItemsAsync(Guid ledgerId, DateOnly from, DateOnly to, CancellationToken ct) =>
        (await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date >= from && i.Date <= to)
            .ToListAsync(ct))
        .Select(i => new Open(i.SupplierId, i.Date, i.DueDate, i.IsDebitNote ? "debit_note" : "invoice", i.InvoiceNo, i.Reference,
            i.IsDebitNote ? -i.PayableTotal : i.PayableTotal))
        .ToList();

    private async Task<int> MinorAsync(Guid ledgerId, CancellationToken ct) =>
        Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;

    private static string F(decimal amount, int minor) => amount.ToString("F" + minor, CultureInfo.InvariantCulture);
}
