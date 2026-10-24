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
/// Accounts Payable reports, from posted purchase documents and payments in one snapshot (owner decision: settle by
/// party). A payment line naming an invoice settles it; debit notes (their original invoice first), party-balance
/// lines and any excess settle the supplier's oldest open invoices; what is left stays as the supplier's credit.
/// </summary>
public sealed class PayablesReportService(IBooksDb db, ReadRunner reads)
{

    public Task<ApAging> AgingAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly asOf, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var minor = await MinorAsync(ledgerId, ct);
            var (charges, credits) = await DocumentsAsync(ledgerId, asOf, ct);
            var items = OpenItems.Settle(charges, credits, asOf);
            var names = await db.Parties.AsNoTracking().Where(p => p.LedgerId == ledgerId).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            decimal[] Buckets(IEnumerable<OpenItem> open) => OpenItems.Buckets(asOf, open);
            AgingRow Row(Guid id, string name, decimal[] b) =>
                new(id, name, F(b[0], minor), F(b[1], minor), F(b[2], minor), F(b[3], minor), F(b[4], minor), F(b.Sum(), minor));

            var rows = items.GroupBy(i => i.PartyId)
                .Select(g => (Id: g.Key, Name: names.GetValueOrDefault(g.Key, "?"), Buckets: Buckets(g)))
                .Where(r => r.Buckets.Sum() != 0)
                .OrderBy(r => r.Name)
                .Select(r => Row(r.Id, r.Name, r.Buckets))
                .ToList();
            var totals = Buckets(items);
            var control = -await OpenItems.ControlBalanceAsync(db, ledgerId, "payable", asOf, ct);
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
            var (charges, credits) = await DocumentsAsync(ledgerId, to, ct);
            var all = OpenItems.Statement(charges, credits).Where(i => i.PartyId == supplierId).ToList();
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

    /// <summary>Posted invoices (charges) and debit notes and payment lines to suppliers (credits) dated up to the date.</summary>
    private async Task<(List<Charge>, List<Credit>)> DocumentsAsync(Guid ledgerId, DateOnly to, CancellationToken ct)
    {
        var documents = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date <= to).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Include(p => p.Lines)
            .Where(p => p.LedgerId == ledgerId && p.Status == DocumentStatus.Posted && p.Date <= to && p.PartyId != null).ToListAsync(ct);
        var payables = await db.AccountUsages.Where(u => u.LedgerId == ledgerId && u.Usage == Usage.Payable).Select(u => u.AccountId).ToListAsync(ct);
        var charges = documents.Where(i => !i.IsDebitNote)
            .Select(i => new Charge(i.Id, i.SupplierId, i.Date, i.DueDate, "invoice", i.InvoiceNo, i.Reference, i.PayableTotal)).ToList();
        var credits = documents.Where(i => i.IsDebitNote)
            .Select(i => new Credit(i.SupplierId, i.Date, "debit_note", i.InvoiceNo, i.Reference, i.OriginalInvoiceId, i.PayableTotal))
            .Concat(OpenItems.PaymentCredits(payments, payables.ToHashSet(), receiptsReduce: false)).ToList();
        return (charges, credits);
    }

    private Task<int> MinorAsync(Guid ledgerId, CancellationToken ct) => OpenItems.MinorAsync(db, ledgerId, ct);

    private static string F(decimal amount, int minor) => OpenItems.F(amount, minor);
}

/// <summary>An amount still open for a party: positive when owed, negative for a credit.</summary>
internal sealed record OpenItem(Guid PartyId, DateOnly Date, DateOnly DueDate, string Type, long Number, string? Reference, decimal Amount);

/// <summary>An invoice: what the party owes (payables) or is owed (receivables).</summary>
internal sealed record Charge(Guid Id, Guid PartyId, DateOnly Date, DateOnly DueDate, string Type, long Number, string? Reference, decimal Amount);

/// <summary>A debit or credit note, or one line of a payment or receipt, reducing the party's balance; it may name the invoice it settles.</summary>
internal sealed record Credit(Guid PartyId, DateOnly Date, string Type, long Number, string? Reference, Guid? InvoiceId, decimal Amount);

/// <summary>Aging and control-balance arithmetic shared by Accounts Payable and Accounts Receivable.</summary>
internal static class OpenItems
{
    private sealed record Total(decimal Amount);

    /// <summary>
    /// Settles each party's invoices (owner decision: by party, Manager.io style). Credits naming an invoice settle it
    /// first; the rest, and any excess, settle the oldest open invoices by due date; what is left is the party's
    /// credit, shown as not due. The open amounts always add up to invoices less credits, so aging equals the control balance.
    /// </summary>
    public static List<OpenItem> Settle(IEnumerable<Charge> charges, IEnumerable<Credit> credits, DateOnly asOf)
    {
        var open = new List<OpenItem>();
        var byParty = credits.GroupBy(c => c.PartyId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var party in charges.GroupBy(c => c.PartyId).Select(g => g.Key).Union(byParty.Keys))
        {
            var invoices = charges.Where(c => c.PartyId == party).OrderBy(c => c.DueDate).ThenBy(c => c.Date).ThenBy(c => c.Number).ToList();
            var remaining = invoices.ToDictionary(c => c.Id, c => c.Amount);
            var pool = 0m;
            foreach (var credit in byParty.GetValueOrDefault(party, []).OrderBy(c => c.Date).ThenBy(c => c.Number))
            {
                if (credit.InvoiceId is { } id && remaining.TryGetValue(id, out var left) && credit.Amount > 0)
                {
                    var take = Math.Min(Math.Max(left, 0), credit.Amount);
                    remaining[id] = left - take;
                    pool += credit.Amount - take;
                }
                else
                {
                    pool += credit.Amount;
                }
            }
            foreach (var invoice in invoices)
            {
                var take = Math.Min(Math.Max(remaining[invoice.Id], 0), Math.Max(pool, 0));
                remaining[invoice.Id] -= take;
                pool -= take;
                if (remaining[invoice.Id] != 0)
                {
                    open.Add(new OpenItem(party, invoice.Date, invoice.DueDate, invoice.Type, invoice.Number, invoice.Reference, remaining[invoice.Id]));
                }
            }
            if (pool != 0)
            {
                open.Add(new OpenItem(party, asOf, asOf, "credit", 0, null, -pool));
            }
        }
        return open;
    }

    /// <summary>Statement items: each invoice owed, and each note, payment or receipt as one credit.</summary>
    public static List<OpenItem> Statement(IEnumerable<Charge> charges, IEnumerable<Credit> credits) =>
        charges.Select(c => new OpenItem(c.PartyId, c.Date, c.DueDate, c.Type, c.Number, c.Reference, c.Amount))
            .Concat(credits.GroupBy(c => (c.PartyId, c.Type, c.Number))
                .Select(g => new OpenItem(g.Key.PartyId, g.First().Date, g.First().Date, g.Key.Type, g.Key.Number, g.First().Reference, -g.Sum(c => c.Amount))))
            .ToList();

    /// <summary>
    /// Lines of posted payments and receipts on the given control accounts, as credits of the document's party. A
    /// payment reduces what is owed to a supplier and a receipt what a customer owes; the other direction (a refund)
    /// adds to it.
    /// </summary>
    public static IEnumerable<Credit> PaymentCredits(IEnumerable<Payment> payments, IReadOnlySet<Guid> controlAccounts, bool receiptsReduce) =>
        payments.SelectMany(p => p.Lines.Where(l => l.Kind != PaymentLineKind.Account && controlAccounts.Contains(l.ControlAccountId!.Value))
            .Select(l => new Credit(p.PartyId!.Value, p.Date, p.IsReceipt ? "receipt" : "payment", p.PaymentNo, p.Reference, l.InvoiceId,
                p.IsReceipt == receiptsReduce ? l.NetAmount : -l.NetAmount)));

    /// <summary>Not due, 1–30, 31–60, 61–90 and over 90 days past the due date.</summary>
    public static decimal[] Buckets(DateOnly asOf, IEnumerable<OpenItem> open)
    {
        var b = new decimal[5];
        foreach (var item in open)
        {
            var overdue = asOf.DayNumber - item.DueDate.DayNumber;
            b[overdue <= 0 ? 0 : overdue <= 30 ? 1 : overdue <= 60 ? 2 : overdue <= 90 ? 3 : 4] += item.Amount;
        }
        return b;
    }

    /// <summary>Debit balance of every account carrying the usage, from effective postings up to the date (rule 43).</summary>
    public static async Task<decimal> ControlBalanceAsync(IBooksDb db, Guid ledgerId, string usage, DateOnly asOf, CancellationToken ct) =>
        (await db.Database.SqlQuery<Total>($"""
            SELECT COALESCE(SUM(l.amount), 0) AS amount
            FROM journals j
            JOIN journal_revisions r ON r.id = j.effective_revision_id
            JOIN journal_lines l ON l.revision_id = r.id
            JOIN account_usages u ON u.account_id = l.account_id AND u.usage = {usage}
            WHERE j.ledger_id = {ledgerId} AND r.accounting_date <= {asOf}
            """).SingleAsync(ct)).Amount;

    public static async Task<int> MinorAsync(IBooksDb db, Guid ledgerId, CancellationToken ct) =>
        Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;

    public static string F(decimal amount, int minor) => amount.ToString("F" + minor, CultureInfo.InvariantCulture);
}
