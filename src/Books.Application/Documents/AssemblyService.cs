using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <param name="LabourAccountId">Where the labour is credited (for example wages or a payable); required with a labour amount.</param>
public sealed record AssemblyInput(DateOnly Date, Guid ItemId, decimal Quantity, decimal Labour, Guid? LabourAccountId, string? Reference, string? Description,
    Guid? LocationId = null);

public sealed record AssemblyLineDto(Guid ComponentId, string Quantity, string Value);

public sealed record AssemblyDto(
    Guid Id, long AssemblyNo, DateOnly Date, Guid ItemId, string Quantity, IReadOnlyList<AssemblyLineDto> Components, string Labour, Guid? LabourAccountId,
    string Value, string UnitCost, string? Reference, string? Description, string Status, Guid? JournalId, string Version, IReadOnlyList<string>? Warnings = null);

/// <summary>
/// Assemblies (M10.10): make a quantity of an item from its bill of materials. Components leave at their average
/// cost on the date; the item comes in at their total plus labour (owner decision). Posted when saved; delete to undo.
/// </summary>
public sealed class AssemblyService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting)
{
    public Task<IReadOnlyList<AssemblyDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<AssemblyDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var minor = await MinorAsync(ledgerId, ct);
            return (await db.Assemblies.AsNoTracking().Include(a => a.Lines).Where(a => a.LedgerId == ledgerId).OrderByDescending(a => a.Date).ThenByDescending(a => a.AssemblyNo)
                .ToListAsync(ct)).Select(a => ToDto(a, minor)).ToList();
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, AssemblyInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("assembly.post", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
            var item = await db.Items.AsNoTracking().Include(i => i.Components).SingleOrDefaultAsync(i => i.LedgerId == ledgerId && i.Id == input.ItemId && i.TrackInventory, ct)
                ?? throw BooksProblem.Invalid("item_id must be an inventory item of this ledger.");
            if (input.LabourAccountId is { } labourAccount && !await db.Accounts.AnyAsync(a => a.LedgerId == ledgerId && a.Id == labourAccount && !a.IsControl, ct))
            {
                throw BooksProblem.Invalid("labour_account_id must be an account of this ledger other than a control account.");
            }
            var assembly = new Assembly(Guid.CreateVersion7(), orgId, ledgerId, await posting.NextNumberAsync(ledgerId, Assembly.Kind, ct), principal.UserId, scope.Now);
            var components = await db.Items.AsNoTracking().Where(i => item.Components.Select(c => c.ComponentId).Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var lines = new List<AssemblyLine>();
            foreach (var component in item.Components)
            {
                var used = component.Quantity * input.Quantity;
                var average = await Inventory.AverageCostAsync(db, ledgerId, component.ComponentId, input.Date, assembly.Id, ct);
                lines.Add(new AssemblyLine(component.ComponentId, used, Math.Round(used * average, minor, MidpointRounding.AwayFromZero),
                    (await Inventory.ItemAccountsAsync(db, ledgerId, components[component.ComponentId], ct)).Inventory));
            }
            try
            {
                assembly.Set(input.Date, item.Id, input.Quantity, (await Inventory.ItemAccountsAsync(db, ledgerId, item, ct)).Inventory, input.Labour, input.LabourAccountId,
                    await Locations.ResolveAsync(db, ledgerId, input.LocationId, "location_id", ct), input.Reference, input.Description, lines);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            var outcome = await posting.PostAsync(scope, ledger, Assembly.Kind, assembly.Id, null, assembly.ToJournal(), ct);
            if (outcome.Journal is null)
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The assembly cannot be posted.") { Issues = outcome.Issues.ToList() };
            }
            assembly.PostedAs(outcome.Journal.Id);
            db.Assemblies.Add(assembly);
            scope.Audit("assembly.posted", Assembly.Kind, assembly.Id, new { assembly.AssemblyNo, item = item.Id, input.Quantity });
            await db.SaveChangesAsync(ct);
            var warnings = await Inventory.NegativeStockAsync(db, ledgerId, item.Components.Select(c => c.ComponentId), input.Date, ct);
            return WriteResult.Of(201, ToDto(assembly, minor) with { Warnings = warnings.Count == 0 ? null : warnings }, assembly.Version);
        }, ct);

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid assemblyId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("assembly.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var assembly = await db.Assemblies.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == assemblyId && a.Status == DocumentStatus.Posted, ct)
                ?? throw BooksProblem.NotFound();
            scope.CheckVersion(assembly.Version);
            await posting.DeleteAsync(scope, await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct), assembly.JournalId!.Value, ct);
            assembly.Delete();
            scope.Audit("assembly.deleted", Assembly.Kind, assembly.Id, new { assembly.AssemblyNo });
            return WriteResult.Of(200, new { id = assembly.Id, deleted = true }, assembly.Version);
        }, ct);

    private async Task<int> MinorAsync(Guid ledgerId, CancellationToken ct) =>
        Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;

    private static AssemblyDto ToDto(Assembly a, int minor)
    {
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        static string Q(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        return new AssemblyDto(a.Id, a.AssemblyNo, a.Date, a.ItemId, Q(a.Quantity),
            a.Lines.OrderBy(l => l.LineNo).Select(l => new AssemblyLineDto(l.ComponentId, Q(l.Quantity), F(l.Value))).ToList(),
            F(a.LabourAmount), a.LabourAccountId, F(a.Value), F(a.Value / a.Quantity), a.Reference, a.Description, Text.Of(a.Status), a.JournalId, a.Version.ToString());
    }
}
