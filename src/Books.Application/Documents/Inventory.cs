using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <summary>One movement of an inventory item: positive quantities and values come in, negative ones go out.</summary>
internal sealed record StockMovement(Guid ItemId, DateOnly Date, Guid DocumentId, decimal Quantity, decimal Value,
    string Kind = "", long Number = 0, string? Reference = null, Guid? PartyId = null);

/// <param name="Kind">The document: purchase_invoice, debit_note, sales_invoice, credit_note or stock_adjustment.</param>
public sealed record ItemMovementLine(
    DateOnly Date, string Kind, Guid DocumentId, long Number, string? Reference, Guid? PartyId, string QuantityIn, string QuantityOut, string Value,
    string BalanceQuantity, string BalanceValue);

/// <summary>An item's movements between two dates with the balance after each, Manager.io and QuickBooks style.</summary>
public sealed record ItemMovements(
    Guid ItemId, string Name, string? Unit, DateOnly From, DateOnly To, string Currency, string OpeningQuantity, string OpeningValue,
    IReadOnlyList<ItemMovementLine> Lines, string ClosingQuantity, string ClosingValue);

public sealed record InventoryRow(Guid ItemId, string Name, string? Unit, string QuantityIn, string QuantityOut, string OnHand, string AverageCost, string Value);

/// <param name="ControlBalance">Balance of the accounts with the Inventory usage; equals the total value when nothing posts to them by hand.</param>
public sealed record InventoryReport(DateOnly AsOf, string Currency, IReadOnlyList<InventoryRow> Items, string TotalValue, string ControlBalance, bool MatchesControl);

/// <summary>
/// Inventory items (owner default: average cost, Manager.io style). Movements are read from posted documents:
/// purchase invoices bring stock in at their net amount and debit notes take it out; sales invoices take it out at
/// the average cost of the stock before them and credit notes bring it back at that cost; stock adjustments move it
/// in or out at the value worked out when they were saved.
/// </summary>
public static class Inventory
{
    internal static async Task<Guid> AccountAsync(IBooksDb db, Guid ledgerId, Usage usage, CancellationToken ct)
    {
        var accounts = await db.AccountUsages.Where(u => u.LedgerId == ledgerId && u.Usage == usage).Select(u => u.AccountId).ToListAsync(ct);
        return accounts.Count == 1
            ? accounts[0]
            : throw BooksProblem.Invalid($"Inventory items need exactly one account with the {Text.Of(usage)} usage; apply the Cambodia template or set one.");
    }

    /// <summary>The item's inventory and cost of sales accounts, or the ledger's when the item has none.</summary>
    internal static async Task<(Guid Inventory, Guid CostOfSales)> ItemAccountsAsync(IBooksDb db, Guid ledgerId, Item item, CancellationToken ct) =>
        (item.InventoryAccountId ?? await AccountAsync(db, ledgerId, Usage.Inventory, ct),
         item.CostOfSalesAccountId ?? await AccountAsync(db, ledgerId, Usage.CostOfSales, ct));

    /// <summary>
    /// Average cost of the item's stock on the date, leaving out one document (the one being saved). When none is on
    /// hand (stock may go negative, owner decision), the average from the last time some was, else the item's
    /// purchase price, else zero.
    /// </summary>
    internal static async Task<decimal> AverageCostAsync(IBooksDb db, Guid ledgerId, Guid itemId, DateOnly date, Guid excludeDocument, CancellationToken ct)
    {
        var movements = (await MovementsAsync(db, ledgerId, date, ct)).Where(m => m.ItemId == itemId && m.DocumentId != excludeDocument).ToList();
        var (quantity, value) = (movements.Sum(m => m.Quantity), movements.Sum(m => m.Value));
        if (quantity > 0)
        {
            return value / quantity;
        }
        decimal? last = null;
        var (runningQuantity, runningValue) = (0m, 0m);
        foreach (var day in movements.GroupBy(m => m.Date).OrderBy(g => g.Key))
        {
            runningQuantity += day.Sum(m => m.Quantity);
            runningValue += day.Sum(m => m.Value);
            if (runningQuantity > 0)
            {
                last = runningValue / runningQuantity;
            }
        }
        return last ?? await db.Items.Where(i => i.Id == itemId).Select(i => i.PurchasePrice).SingleAsync(ct) ?? 0;
    }

    /// <summary>Quantity on hand and its value on the date, leaving out one document (the one being saved).</summary>
    internal static async Task<(decimal Quantity, decimal Value)> PositionAsync(IBooksDb db, Guid ledgerId, Guid itemId, DateOnly date, Guid excludeDocument, CancellationToken ct)
    {
        var movements = (await MovementsAsync(db, ledgerId, date, ct)).Where(m => m.ItemId == itemId && m.DocumentId != excludeDocument).ToList();
        return (movements.Sum(m => m.Quantity), movements.Sum(m => m.Value));
    }

    internal static async Task<List<StockMovement>> MovementsAsync(IBooksDb db, Guid ledgerId, DateOnly to, CancellationToken ct)
    {
        var tracked = await db.Items.Where(i => i.LedgerId == ledgerId && i.TrackInventory).Select(i => i.Id).ToListAsync(ct);
        var purchases = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date <= to).ToListAsync(ct);
        var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
        var sales = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date <= to).ToListAsync(ct);
        var adjustments = await db.StockAdjustments.AsNoTracking().Include(a => a.Lines)
            .Where(a => a.LedgerId == ledgerId && a.Status == DocumentStatus.Posted && a.Date <= to).ToListAsync(ct);
        return purchases.SelectMany(p => p.Lines.Where(l => l.ItemId is { } id && tracked.Contains(id))
                .Select(l => new StockMovement(l.ItemId!.Value, p.Date, p.Id, (p.IsDebitNote ? -1 : 1) * (l.BaseQuantity ?? 0), (p.IsDebitNote ? -1 : 1) * p.ToBase(l.NetAmount, minor),
                    p.IsDebitNote ? "debit_note" : "purchase_invoice", p.InvoiceNo, p.Reference, p.SupplierId)))
            .Concat(sales.SelectMany(s => s.Lines.Where(l => l.ItemId is { } id && tracked.Contains(id))
                .Select(l => new StockMovement(l.ItemId!.Value, s.Date, s.Id, (s.IsCreditNote ? 1 : -1) * (l.BaseQuantity ?? 0), (s.IsCreditNote ? 1 : -1) * l.CostAmount,
                    s.IsCreditNote ? "credit_note" : "sales_invoice", s.InvoiceNo, s.Reference, s.CustomerId))))
            .Concat(adjustments.SelectMany(a => a.Lines.Select(l => new StockMovement(l.ItemId, a.Date, a.Id, l.Quantity, l.Value,
                StockAdjustment.Kind, a.AdjustmentNo, a.Reference))))
            .ToList();
    }

    /// <summary>
    /// Owner decision: stock may go negative, with a warning. For each item, the first date on or after the given date
    /// when its quantity on hand is below zero, as a sentence for the document's warnings; none when it never is.
    /// </summary>
    internal static async Task<List<string>> NegativeStockAsync(IBooksDb db, Guid ledgerId, IEnumerable<Guid> itemIds, DateOnly from, CancellationToken ct)
    {
        var ids = itemIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }
        var names = await db.Items.Where(i => ids.Contains(i.Id) && i.TrackInventory).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        var movements = (await MovementsAsync(db, ledgerId, DateOnly.MaxValue, ct)).Where(m => names.ContainsKey(m.ItemId)).ToList();
        var warnings = new List<string>();
        foreach (var (id, name) in names)
        {
            var running = 0m;
            foreach (var day in movements.Where(m => m.ItemId == id).GroupBy(m => m.Date).OrderBy(g => g.Key))
            {
                running += day.Sum(m => m.Quantity);
                if (day.Key >= from && running < 0)
                {
                    warnings.Add($"{name} goes below zero on {day.Key:yyyy-MM-dd} ({running.ToString("0.####", CultureInfo.InvariantCulture)} on hand); "
                        + "its cost is taken at the average cost so far until stock comes in.");
                    break;
                }
            }
        }
        return warnings;
    }
}

public sealed class InventoryReportService(IBooksDb db, ReadRunner reads)
{
    private sealed record Total(decimal Amount);

    public Task<ItemMovements> MovementsAsync(Principal principal, Guid orgId, Guid ledgerId, Guid itemId, DateOnly from, DateOnly to, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            if (from > to)
            {
                throw BooksProblem.Invalid("from must be on or before to.");
            }
            var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(i => i.LedgerId == ledgerId && i.Id == itemId && i.TrackInventory, ct)
                ?? throw BooksProblem.NotFound();
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            var minor = Currencies.Require(currency).MinorUnits;
            string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
            static string Q(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            var movements = (await Inventory.MovementsAsync(db, ledgerId, to, ct)).Where(m => m.ItemId == itemId).ToList();
            var opening = movements.Where(m => m.Date < from).ToList();
            var (quantity, value) = (opening.Sum(m => m.Quantity), opening.Sum(m => m.Value));
            var (openingQuantity, openingValue) = (quantity, value);
            var lines = movements.Where(m => m.Date >= from).OrderBy(m => m.Date).ThenBy(m => m.Kind).ThenBy(m => m.Number).Select(m =>
            {
                quantity += m.Quantity;
                value += m.Value;
                return new ItemMovementLine(m.Date, m.Kind, m.DocumentId, m.Number, m.Reference, m.PartyId,
                    m.Quantity > 0 ? Q(m.Quantity) : "", m.Quantity < 0 ? Q(-m.Quantity) : "", F(m.Value), Q(quantity), F(value));
            }).ToList();
            return new ItemMovements(item.Id, item.Name, item.Unit, from, to, currency, Q(openingQuantity), F(openingValue), lines, Q(quantity), F(value));
        }, ct);

    public Task<InventoryReport> ReportAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly asOf, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            var minor = Currencies.Require(currency).MinorUnits;
            string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
            static string Q(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            var movements = await Inventory.MovementsAsync(db, ledgerId, asOf, ct);
            var items = await db.Items.AsNoTracking().Where(i => i.LedgerId == ledgerId && i.TrackInventory).OrderBy(i => i.Name).ToListAsync(ct);
            var rows = items.Select(i =>
            {
                var mine = movements.Where(m => m.ItemId == i.Id).ToList();
                var onHand = mine.Sum(m => m.Quantity);
                var value = mine.Sum(m => m.Value);
                return new InventoryRow(i.Id, i.Name, i.Unit, Q(mine.Where(m => m.Quantity > 0).Sum(m => m.Quantity)), Q(-mine.Where(m => m.Quantity < 0).Sum(m => m.Quantity)),
                    Q(onHand), onHand > 0 ? F(Math.Round(value / onHand, minor, MidpointRounding.AwayFromZero)) : F(0), F(value));
            }).ToList();
            var total = movements.Sum(m => m.Value);
            var control = (await db.Database.SqlQuery<Total>($"""
                SELECT COALESCE(SUM(l.amount), 0) AS amount
                FROM journals j
                JOIN journal_revisions r ON r.id = j.effective_revision_id
                JOIN journal_lines l ON l.revision_id = r.id
                JOIN account_usages u ON u.account_id = l.account_id AND u.usage = 'inventory'
                WHERE j.ledger_id = {ledgerId} AND r.accounting_date <= {asOf}
                """).SingleAsync(ct)).Amount;
            return new InventoryReport(asOf, currency, rows, F(total), F(control), total == control);
        }, ct);
}
