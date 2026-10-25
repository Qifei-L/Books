using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record LocationInput(string Name, string? Code, bool IsDefault = false, bool Active = true);

public sealed record LocationDto(Guid Id, string Name, string? Code, bool IsDefault, bool Active, string Version);

public sealed record StockTransferLineInput(Guid ItemId, decimal Quantity, string? Description);

public sealed record StockTransferInput(DateOnly Date, Guid FromLocationId, Guid ToLocationId, string? Reference, string? Description, IReadOnlyList<StockTransferLineInput> Lines);

public sealed record StockTransferLineDto(int LineNo, Guid ItemId, string Quantity, string? Description);

public sealed record StockTransferDto(
    Guid Id, long TransferNo, DateOnly Date, Guid FromLocationId, Guid ToLocationId, string? Reference, string? Description,
    IReadOnlyList<StockTransferLineDto> Lines, string Status, string Version);

/// <summary>Quantity of one item at one location on a date; the value is one average per item across locations.</summary>
public sealed record LocationStockRow(Guid ItemId, string Item, string? Unit, Guid? LocationId, string? Location, string Quantity);

public sealed record LocationStock(DateOnly AsOf, IReadOnlyList<LocationStockRow> Rows);

/// <summary>
/// Inventory locations and stock transfers between them (M10.9, Manager.io style). The first location becomes the
/// default; lines naming no location use it. Transfers move quantities only (one average cost across locations).
/// </summary>
public sealed class LocationService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting)
{
    public Task<IReadOnlyList<LocationDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<LocationDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await db.Locations.AsNoTracking().Where(l => l.LedgerId == ledgerId).OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name).ToListAsync(ct))
                .Select(ToDto).ToList();
        }, ct);

    public Task<WriteResult> SaveAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid? locationId, LocationInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("location.save", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var all = await db.Locations.Where(l => l.LedgerId == ledgerId).ToListAsync(ct);
            Location location;
            if (locationId is { } id)
            {
                location = all.SingleOrDefault(l => l.Id == id) ?? throw BooksProblem.NotFound();
                scope.CheckVersion(location.Version);
            }
            else
            {
                location = new Location(Guid.CreateVersion7(), ledger, scope.Now);
                db.Locations.Add(location);
                all.Add(location);
            }
            // Exactly one default: the first location, or the one made default; the default cannot be unset directly.
            if (location.IsDefault && !input.IsDefault && all.Count > 1)
            {
                throw BooksProblem.Invalid("Make another location the default instead.");
            }
            var isDefault = input.IsDefault || all.Count == 1 || location.IsDefault;
            if (all.Any(l => l.Id != location.Id && l.Name.Equals(input.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                throw BooksProblem.Conflict("LOCATION_EXISTS", $"There is already a location named {input.Name.Trim()}.");
            }
            try
            {
                location.Set(input.Name, input.Code, isDefault, input.Active, isNew: locationId is null);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            if (isDefault)
            {
                foreach (var other in all.Where(l => l.Id != location.Id))
                {
                    other.NotDefault();
                }
            }
            scope.Audit("location.saved", "location", location.Id, ToDto(location));
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(locationId is null ? 201 : 200, ToDto(location), location.Version);
        }, ct);

    public Task<IReadOnlyList<StockTransferDto>> TransfersAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<StockTransferDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await db.StockTransfers.AsNoTracking().Include(t => t.Lines).Where(t => t.LedgerId == ledgerId)
                    .OrderByDescending(t => t.Date).ThenByDescending(t => t.TransferNo).ToListAsync(ct))
                .Select(ToDto).ToList();
        }, ct);

    public Task<WriteResult> SaveTransferAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid? transferId, StockTransferInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("stock_transfer.save", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            if (ledger.ClosedThrough is { } closed && input.Date <= closed)
            {
                throw BooksProblem.Conflict("DATE_CLOSED", $"The books are closed through {closed:yyyy-MM-dd}.");
            }
            StockTransfer transfer;
            if (transferId is { } id)
            {
                transfer = await db.StockTransfers.Include(t => t.Lines).SingleOrDefaultAsync(t => t.LedgerId == ledgerId && t.Id == id && t.Status == DocumentStatus.Posted, ct)
                    ?? throw BooksProblem.NotFound();
                scope.CheckVersion(transfer.Version);
                if (ledger.ClosedThrough is { } closedBefore && transfer.Date <= closedBefore)
                {
                    throw BooksProblem.Conflict("DATE_CLOSED", $"The books are closed through {closedBefore:yyyy-MM-dd}.");
                }
                db.StockTransferLines.RemoveRange(transfer.Lines);
            }
            else
            {
                transfer = new StockTransfer(Guid.CreateVersion7(), orgId, ledgerId, await posting.NextNumberAsync(ledgerId, StockTransfer.Kind, ct), principal.UserId, scope.Now);
                db.StockTransfers.Add(transfer);
            }
            var locations = await db.Locations.Where(l => l.LedgerId == ledgerId && l.Active && (l.Id == input.FromLocationId || l.Id == input.ToLocationId)).Select(l => l.Id).ToListAsync(ct);
            if (!locations.Contains(input.FromLocationId) || !locations.Contains(input.ToLocationId))
            {
                throw BooksProblem.Invalid("from_location_id and to_location_id must be active locations of this ledger.");
            }
            var itemIds = (input.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select(l => l.ItemId).ToList();
            var tracked = await db.Items.Where(i => i.LedgerId == ledgerId && i.TrackInventory && itemIds.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
            if (itemIds.Except(tracked).Any())
            {
                throw BooksProblem.Invalid("Each line needs an inventory item of this ledger.");
            }
            try
            {
                transfer.Set(input.Date, input.FromLocationId, input.ToLocationId, input.Reference, input.Description,
                    input.Lines.Select(l => new StockTransferLine(l.ItemId, l.Quantity, l.Description)), isNew: transferId is null);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            scope.Audit("stock_transfer.saved", StockTransfer.Kind, transfer.Id, ToDto(transfer));
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(transferId is null ? 201 : 200, ToDto(transfer), transfer.Version);
        }, ct);

    public Task<WriteResult> DeleteTransferAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid transferId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("stock_transfer.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var transfer = await db.StockTransfers.SingleOrDefaultAsync(t => t.LedgerId == ledgerId && t.Id == transferId && t.Status == DocumentStatus.Posted, ct)
                ?? throw BooksProblem.NotFound();
            scope.CheckVersion(transfer.Version);
            var closed = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.ClosedThrough).SingleAsync(ct);
            if (closed is { } through && transfer.Date <= through)
            {
                throw BooksProblem.Conflict("DATE_CLOSED", $"The books are closed through {through:yyyy-MM-dd}.");
            }
            transfer.Delete();
            scope.Audit("stock_transfer.deleted", StockTransfer.Kind, transfer.Id, new { transfer.TransferNo });
            return WriteResult.Of(200, new { id = transfer.Id, deleted = true }, transfer.Version);
        }, ct);

    /// <summary>Quantity of each inventory item at each location on the date.</summary>
    public Task<LocationStock> StockAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly asOf, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var items = await db.Items.AsNoTracking().Where(i => i.LedgerId == ledgerId && i.TrackInventory).ToDictionaryAsync(i => i.Id, ct);
            var locations = await db.Locations.AsNoTracking().Where(l => l.LedgerId == ledgerId).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
            var rows = (await Inventory.MovementsAsync(db, ledgerId, asOf, ct))
                .GroupBy(m => (m.ItemId, m.LocationId))
                .Select(g => (g.Key.ItemId, g.Key.LocationId, Quantity: g.Sum(m => m.Quantity)))
                .Where(r => r.Quantity != 0)
                .OrderBy(r => items[r.ItemId].Name).ThenBy(r => r.LocationId is { } l ? locations.GetValueOrDefault(l) : "")
                .Select(r => new LocationStockRow(r.ItemId, items[r.ItemId].Name, items[r.ItemId].Unit, r.LocationId,
                    r.LocationId is { } l ? locations.GetValueOrDefault(l) : null, r.Quantity.ToString("0.####", CultureInfo.InvariantCulture)))
                .ToList();
            return new LocationStock(asOf, rows);
        }, ct);

    private static LocationDto ToDto(Location l) => new(l.Id, l.Name, l.Code, l.IsDefault, l.Active, l.Version.ToString(CultureInfo.InvariantCulture));

    private static StockTransferDto ToDto(StockTransfer t) => new(t.Id, t.TransferNo, t.Date, t.FromLocationId, t.ToLocationId, t.Reference, t.Description,
        t.Lines.OrderBy(l => l.LineNo).Select(l => new StockTransferLineDto(l.LineNo, l.ItemId, l.Quantity.ToString("0.####", CultureInfo.InvariantCulture), l.Description)).ToList(),
        Text.Of(t.Status), t.Version.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Which location a line uses.</summary>
internal static class Locations
{
    /// <summary>The location given (an active one of the ledger), else the ledger's default; null when the ledger has none.</summary>
    public static async Task<Guid?> ResolveAsync(IBooksDb db, Guid ledgerId, Guid? locationId, string field, CancellationToken ct)
    {
        if (locationId is { } id)
        {
            return await db.Locations.AnyAsync(l => l.LedgerId == ledgerId && l.Id == id && l.Active, ct)
                ? id
                : throw BooksProblem.Invalid($"{field} must be an active location of this ledger.");
        }
        return await db.Locations.Where(l => l.LedgerId == ledgerId && l.IsDefault).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
    }
}
