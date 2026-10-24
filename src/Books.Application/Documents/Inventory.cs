using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <summary>One movement of an inventory item: positive quantities and values come in, negative ones go out.</summary>
internal sealed record StockMovement(Guid ItemId, DateOnly Date, Guid DocumentId, decimal Quantity, decimal Value);

public sealed record InventoryRow(Guid ItemId, string Name, string? Unit, string QuantityIn, string QuantityOut, string OnHand, string AverageCost, string Value);

/// <param name="ControlBalance">Balance of the accounts with the Inventory usage; equals the total value when nothing posts to them by hand.</param>
public sealed record InventoryReport(DateOnly AsOf, string Currency, IReadOnlyList<InventoryRow> Items, string TotalValue, string ControlBalance, bool MatchesControl);

/// <summary>
/// Inventory items (owner default: average cost, Manager.io style). Movements are read from posted documents:
/// purchase invoices bring stock in at their net amount and debit notes take it out; sales invoices take it out at
/// the average cost of the stock before them and credit notes bring it back at that cost.
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

    /// <summary>Average cost of the item's stock on the date, leaving out one document (the one being saved).</summary>
    internal static async Task<decimal> AverageCostAsync(IBooksDb db, Guid ledgerId, Guid itemId, DateOnly date, Guid excludeDocument, CancellationToken ct)
    {
        var movements = (await MovementsAsync(db, ledgerId, date, ct)).Where(m => m.ItemId == itemId && m.DocumentId != excludeDocument).ToList();
        var quantity = movements.Sum(m => m.Quantity);
        return quantity > 0 ? movements.Sum(m => m.Value) / quantity : 0;
    }

    internal static async Task<List<StockMovement>> MovementsAsync(IBooksDb db, Guid ledgerId, DateOnly to, CancellationToken ct)
    {
        var tracked = await db.Items.Where(i => i.LedgerId == ledgerId && i.TrackInventory).Select(i => i.Id).ToListAsync(ct);
        var purchases = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date <= to).ToListAsync(ct);
        var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
        var sales = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date <= to).ToListAsync(ct);
        return purchases.SelectMany(p => p.Lines.Where(l => l.ItemId is { } id && tracked.Contains(id))
                .Select(l => new StockMovement(l.ItemId!.Value, p.Date, p.Id, (p.IsDebitNote ? -1 : 1) * (l.Quantity ?? 0), (p.IsDebitNote ? -1 : 1) * p.ToBase(l.NetAmount, minor))))
            .Concat(sales.SelectMany(s => s.Lines.Where(l => l.ItemId is { } id && tracked.Contains(id))
                .Select(l => new StockMovement(l.ItemId!.Value, s.Date, s.Id, (s.IsCreditNote ? 1 : -1) * (l.Quantity ?? 0), (s.IsCreditNote ? 1 : -1) * l.CostAmount))))
            .ToList();
    }
}

public sealed class InventoryReportService(IBooksDb db, ReadRunner reads)
{
    private sealed record Total(decimal Amount);

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
