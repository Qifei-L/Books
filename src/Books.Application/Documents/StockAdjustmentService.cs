using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <param name="Quantity">Signed change: positive comes in, negative goes out. Give this or CountedQuantity.</param>
/// <param name="CountedQuantity">The quantity counted on the date; the change is counted minus on hand.</param>
/// <param name="UnitCost">Cost of stock coming in; omit to use the average cost on the date.</param>
/// <param name="AccountId">The other side; omit for the ledger's cost of sales account.</param>
public sealed record StockAdjustmentLineInput(Guid ItemId, string? Description, decimal? Quantity, decimal? CountedQuantity, decimal? UnitCost, Guid? AccountId);

public sealed record StockAdjustmentInput(DateOnly Date, string? Reference, string? Description, IReadOnlyList<StockAdjustmentLineInput> Lines);

public sealed record StockAdjustmentLineDto(
    int LineNo, Guid ItemId, string? Description, string Quantity, string? CountedQuantity, string BookQuantity, string? UnitCost, string Value, Guid AccountId);

public sealed record StockAdjustmentDto(
    Guid Id, Guid LedgerId, long AdjustmentNo, DateOnly Date, string? Reference, string? Description, IReadOnlyList<StockAdjustmentLineDto> Lines,
    string TotalValue, string Status, IReadOnlyList<JournalIssue> Issues, Guid? JournalId, long? JournalNo, string Version);

/// <summary>
/// Stock adjustments and counts (M10.3). Values are worked out when the adjustment is saved, from the stock on its
/// date without the adjustment itself, and stay as saved, like the cost of sales on sales invoices.
/// </summary>
public sealed class StockAdjustmentService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting)
{
    public Task<IReadOnlyList<StockAdjustmentDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, StockAdjustmentInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "stock_adjustment.save" : "stock_adjustment.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var adjustment = new StockAdjustment(Guid.CreateVersion7(), orgId, ledgerId, await posting.NextNumberAsync(ledgerId, StockAdjustment.Kind, ct), principal.UserId, scope.Now);
            await SetAsync(adjustment, ledger, input, isNew: true, ct);
            db.StockAdjustments.Add(adjustment);
            var status = await SaveAsync(scope, ledger, adjustment, post, ct);
            scope.Audit($"stock_adjustment.{status}", "stock_adjustment", adjustment.Id,
                new { adjustment_no = adjustment.AdjustmentNo, journal_id = adjustment.JournalId, issues = adjustment.Issues });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(status == "not_posted" ? 422 : 201, (await LoadAsync(ledgerId, adjustment.Id, ct)).Single(), adjustment.Version);
        }, ct);

    /// <summary>A posted adjustment whose change cannot be posted stays unchanged (rule 28).</summary>
    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid adjustmentId, StockAdjustmentInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "stock_adjustment.save" : "stock_adjustment.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var adjustment = await FindAsync(ledgerId, adjustmentId, ct);
            scope.CheckVersion(adjustment.Version);
            var wasPosted = adjustment.Status == DocumentStatus.Posted;
            if (wasPosted && !post)
            {
                throw BooksProblem.Conflict("DOCUMENT_POSTED", "A posted document cannot go back to draft; save it or delete it.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var before = (await LoadAsync(ledgerId, adjustment.Id, ct)).Single();
            db.StockAdjustmentLines.RemoveRange(adjustment.Lines);
            await SetAsync(adjustment, ledger, input, isNew: false, ct);
            var status = await SaveAsync(scope, ledger, adjustment, post, ct);
            if (wasPosted && status == "not_posted")
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The change cannot be posted; the adjustment and its posting are unchanged.")
                {
                    Issues = System.Text.Json.JsonSerializer.Deserialize<List<JournalIssue>>(adjustment.Issues!, BooksJson.Options),
                };
            }
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, adjustment.Id, ct)).Single();
            scope.Audit($"stock_adjustment.{status}", "stock_adjustment", adjustment.Id, new { before, after });
            return WriteResult.Of(status == "not_posted" ? 422 : 200, after, adjustment.Version);
        }, ct);

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid adjustmentId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("stock_adjustment.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var adjustment = await FindAsync(ledgerId, adjustmentId, ct);
            scope.CheckVersion(adjustment.Version);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            if (adjustment.Status == DocumentStatus.Posted)
            {
                await posting.DeleteAsync(scope, ledger, adjustment.JournalId!.Value, ct);
            }
            adjustment.Delete();
            scope.Audit("stock_adjustment.deleted", "stock_adjustment", adjustment.Id, new { adjustment_no = adjustment.AdjustmentNo });
            return WriteResult.Of(200, new { id = adjustment.Id, deleted = true }, adjustment.Version);
        }, ct);

    private async Task<string> SaveAsync(WriteScope scope, Ledger ledger, StockAdjustment adjustment, bool post, CancellationToken ct)
    {
        if (!post)
        {
            adjustment.KeptAsDraft(Issues(await posting.CheckAsync(ledger, adjustment.ToJournal(), ct)));
            return "draft";
        }
        var outcome = await posting.PostAsync(scope, ledger, StockAdjustment.Kind, adjustment.Id, adjustment.JournalId, adjustment.ToJournal(), ct);
        if (outcome.Journal is { } journal)
        {
            adjustment.PostedAs(journal.Id);
            return "posted";
        }
        adjustment.KeptAsDraft(Issues(outcome.Issues));
        return "not_posted";
    }

    private static string? Issues(IReadOnlyList<JournalIssue> issues) => issues.Count == 0 ? null : BooksJson.Serialize(issues);

    private async Task<StockAdjustment> FindAsync(Guid ledgerId, Guid adjustmentId, CancellationToken ct)
    {
        var adjustment = await db.StockAdjustments.Include(a => a.Lines).SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == adjustmentId, ct)
            ?? throw BooksProblem.NotFound();
        return adjustment.Status == DocumentStatus.Deleted ? throw BooksProblem.Conflict("DOCUMENT_DELETED", "This adjustment was deleted.") : adjustment;
    }

    /// <summary>
    /// Works out each line from the stock on the date without this adjustment. Stock going out is valued at the
    /// average cost, and taking out everything on hand takes out its whole value, so no remainder is left behind.
    /// </summary>
    private async Task SetAsync(StockAdjustment adjustment, Ledger ledger, StockAdjustmentInput input, bool isNew, CancellationToken ct)
    {
        var lines = input.Lines ?? throw BooksProblem.Invalid("lines is required.");
        if (lines.GroupBy(l => l.ItemId).Any(g => g.Count() > 1))
        {
            throw BooksProblem.Invalid("Each item may appear once per adjustment.");
        }
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var itemIds = lines.Select(l => l.ItemId).ToList();
        var items = await db.Items.Where(i => i.LedgerId == ledger.Id && itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var inventoryAccount = await Inventory.AccountAsync(db, ledger.Id, Usage.Inventory, ct);
        Guid? costOfSales = null;
        var built = new List<StockAdjustmentLine>();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (!items.TryGetValue(line.ItemId, out var item) || !item.TrackInventory)
            {
                throw BooksProblem.Invalid($"lines[{i}].item_id must be an inventory item of this ledger.");
            }
            if ((line.Quantity is null) == (line.CountedQuantity is null))
            {
                throw BooksProblem.Invalid($"lines[{i}] needs quantity or counted_quantity, not both.");
            }
            if (line.CountedQuantity < 0 || line.UnitCost < 0)
            {
                throw BooksProblem.Invalid($"lines[{i}]: counted_quantity and unit_cost cannot be negative.");
            }
            var (onHand, onHandValue) = await Inventory.PositionAsync(db, ledger.Id, item.Id, input.Date, adjustment.Id, ct);
            var change = line.Quantity ?? line.CountedQuantity!.Value - onHand;
            if (change == 0)
            {
                throw BooksProblem.Invalid($"lines[{i}]: {item.Name} already has {Q(onHand)} on hand on {input.Date:yyyy-MM-dd}; leave the line out.");
            }
            if (change < 0 && line.UnitCost is not null)
            {
                throw BooksProblem.Invalid($"lines[{i}].unit_cost applies only to stock coming in; stock going out is valued at the average cost.");
            }
            if (onHand + change < 0)
            {
                throw BooksProblem.Invalid($"lines[{i}]: only {Q(onHand)} of {item.Name} is on hand on {input.Date:yyyy-MM-dd}.");
            }
            var average = onHand > 0 ? onHandValue / onHand : 0;
            var value = change < 0 && onHand + change == 0 ? -onHandValue
                : Math.Round(change * (change > 0 ? line.UnitCost ?? average : average), minor, MidpointRounding.AwayFromZero);
            var account = line.AccountId ?? (costOfSales ??= await Inventory.AccountAsync(db, ledger.Id, Usage.CostOfSales, ct));
            await CheckAccountAsync(ledger.Id, account, inventoryAccount, i, ct);
            try
            {
                built.Add(new StockAdjustmentLine(item.Id, line.Description, change, line.CountedQuantity, onHand, line.UnitCost, value, account, inventoryAccount));
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid($"lines[{i}]: {e.Message}");
            }
        }
        try
        {
            adjustment.Set(input.Date, input.Reference, input.Description, built, isNew);
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }

    private async Task CheckAccountAsync(Guid ledgerId, Guid accountId, Guid inventoryAccount, int i, CancellationToken ct)
    {
        var ok = accountId != inventoryAccount
            && await db.Accounts.AnyAsync(a => a.LedgerId == ledgerId && a.Id == accountId && !a.IsControl, ct)
            && !await db.BankAccounts.AnyAsync(b => b.LedgerId == ledgerId && b.AccountId == accountId, ct);
        if (!ok)
        {
            throw BooksProblem.Invalid($"lines[{i}].account_id must be an account of this ledger other than inventory, a bank or a control account.");
        }
    }

    private static string Q(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private async Task<IReadOnlyList<StockAdjustmentDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct)
    {
        var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        static string? N(decimal? v) => v is { } x ? Q(x) : null;
        return (await db.StockAdjustments.AsNoTracking().Include(a => a.Lines).Where(a => a.LedgerId == ledgerId && (id == null || a.Id == id))
                .OrderByDescending(a => a.Date).ThenByDescending(a => a.AdjustmentNo)
                .Select(a => new { a, JournalNo = db.Journals.Where(j => j.Id == a.JournalId).Select(j => (long?)j.JournalNo).FirstOrDefault() })
                .ToListAsync(ct))
            .Select(x => new StockAdjustmentDto(x.a.Id, x.a.LedgerId, x.a.AdjustmentNo, x.a.Date, x.a.Reference, x.a.Description,
                x.a.Lines.OrderBy(l => l.LineNo).Select(l => new StockAdjustmentLineDto(l.LineNo, l.ItemId, l.Description, Q(l.Quantity), N(l.CountedQuantity),
                    Q(l.BookQuantity), N(l.UnitCost), F(l.Value), l.AccountId)).ToList(),
                F(x.a.Lines.Sum(l => l.Value)), Text.Of(x.a.Status),
                x.a.Issues is null ? [] : System.Text.Json.JsonSerializer.Deserialize<List<JournalIssue>>(x.a.Issues, BooksJson.Options)!,
                x.a.JournalId, x.JournalNo, x.a.Version.ToString()))
            .ToList();
    }
}
