using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reporting;

public sealed record ArAgingRow(Guid CustomerId, string Customer, string NotDue, string Days1To30, string Days31To60, string Days61To90, string Over90, string Total);

/// <param name="ControlBalance">Debit balance of all Receivable accounts as of the date, from the journals.</param>
/// <param name="MatchesControl">Rule 43: the aging total equals the control balance.</param>
public sealed record ArAging(DateOnly AsOf, string Currency, IReadOnlyList<ArAgingRow> Rows, ArAgingRow Totals, string ControlBalance, bool MatchesControl);

public sealed record CustomerStatement(
    Guid CustomerId, string Customer, DateOnly From, DateOnly To, string Currency, string OpeningBalance, IReadOnlyList<StatementItem> Items, string ClosingBalance);

/// <summary>
/// Accounts Receivable reports, from posted sales documents in one snapshot. An invoice is open for its gross
/// total; a credit note is a credit. Receipts (M7) will reduce these amounts once they exist.
/// </summary>
public sealed class ReceivablesReportService(IBooksDb db, ReadRunner reads)
{
    public Task<ArAging> AgingAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly asOf, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var minor = await OpenItems.MinorAsync(db, ledgerId, ct);
            var items = await OpenItemsAsync(ledgerId, asOf, ct);
            var names = await db.Parties.AsNoTracking().Where(p => p.LedgerId == ledgerId).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            ArAgingRow Row(Guid id, string name, decimal[] b) =>
                new(id, name, OpenItems.F(b[0], minor), OpenItems.F(b[1], minor), OpenItems.F(b[2], minor), OpenItems.F(b[3], minor), OpenItems.F(b[4], minor),
                    OpenItems.F(b.Sum(), minor));

            var rows = items.GroupBy(i => i.PartyId)
                .Select(g => (Id: g.Key, Name: names.GetValueOrDefault(g.Key, "?"), Buckets: OpenItems.Buckets(asOf, g)))
                .Where(r => r.Buckets.Sum() != 0)
                .OrderBy(r => r.Name)
                .Select(r => Row(r.Id, r.Name, r.Buckets))
                .ToList();
            var totals = OpenItems.Buckets(asOf, items);
            var control = await OpenItems.ControlBalanceAsync(db, ledgerId, "receivable", asOf, ct);
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            return new ArAging(asOf, currency, rows, Row(Guid.Empty, "Total", totals), OpenItems.F(control, minor), control == totals.Sum());
        }, ct);

    public Task<CustomerStatement> StatementAsync(Principal principal, Guid orgId, Guid ledgerId, Guid customerId, DateOnly from, DateOnly to, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            if (from > to)
            {
                throw BooksProblem.Invalid("from must be on or before to.");
            }
            var customer = await db.Parties.AsNoTracking().SingleOrDefaultAsync(p => p.LedgerId == ledgerId && p.Id == customerId, ct) ?? throw BooksProblem.NotFound();
            var minor = await OpenItems.MinorAsync(db, ledgerId, ct);
            var all = (await OpenItemsAsync(ledgerId, to, ct)).Where(i => i.PartyId == customerId).ToList();
            var opening = all.Where(i => i.Date < from).Sum(i => i.Amount);
            var running = opening;
            var items = all.Where(i => i.Date >= from).OrderBy(i => i.Date).ThenBy(i => i.Type).ThenBy(i => i.Number).Select(i =>
            {
                running += i.Amount;
                return new StatementItem(i.Date, i.Type, i.Number, i.Reference, i.DueDate, OpenItems.F(i.Amount, minor), OpenItems.F(running, minor));
            }).ToList();
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            return new CustomerStatement(customer.Id, customer.Name, from, to, currency, OpenItems.F(opening, minor), items, OpenItems.F(running, minor));
        }, ct);

    /// <summary>Posted invoices (owed, positive) and credit notes (negative) dated up to the date.</summary>
    private async Task<List<OpenItem>> OpenItemsAsync(Guid ledgerId, DateOnly to, CancellationToken ct) =>
        (await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date <= to)
            .ToListAsync(ct))
        .Select(i => new OpenItem(i.CustomerId, i.Date, i.DueDate, i.IsCreditNote ? "credit_note" : "invoice", i.InvoiceNo, i.Reference,
            i.IsCreditNote ? -i.GrossTotal : i.GrossTotal))
        .ToList();
}
