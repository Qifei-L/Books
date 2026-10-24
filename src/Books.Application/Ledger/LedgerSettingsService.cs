using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

/// <summary>Ledger setup and Close. Only administrators change them (rule 13 and owner decisions).</summary>
public sealed class LedgerSettingsService(IBooksDb db, WriteRunner writes)
{
    public Task<WriteResult> ChangeSettingsAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, string? baseCurrency, int? fyStartMonth, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("ledger.settings", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            var ledger = await db.Ledgers.SingleOrDefaultAsync(l => l.OrgId == orgId && l.Id == ledgerId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(ledger.Version);
            var before = new { base_currency = ledger.BaseCurrency, fy_start_month = ledger.FyStartMonth };
            ledger.ChangeSettings(baseCurrency, fyStartMonth);
            scope.Audit("ledger.settings_changed", "ledger", ledger.Id, new { before, after = new { base_currency = ledger.BaseCurrency, fy_start_month = ledger.FyStartMonth } });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, await LedgerAsync(ledgerId, ct), ledger.Version);
        }, ct);

    /// <summary>Sets, moves back or clears the inclusive Close date. Journals dated on or before it cannot change.</summary>
    public Task<WriteResult> CloseAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, DateOnly? closedThrough, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("ledger.close", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            var ledger = await db.Ledgers.SingleOrDefaultAsync(l => l.OrgId == orgId && l.Id == ledgerId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(ledger.Version);
            var before = ledger.ClosedThrough;
            ledger.CloseThrough(closedThrough);
            var action = before is not null && (closedThrough is null || closedThrough < before) ? "ledger.reopened" : "ledger.closed";
            scope.Audit(action, "ledger", ledger.Id, new { before, after = closedThrough });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, await LedgerAsync(ledgerId, ct), ledger.Version);
        }, ct);

    private async Task<LedgerDto> LedgerAsync(Guid ledgerId, CancellationToken ct)
    {
        var row = await db.Ledgers.AsNoTracking().Where(l => l.Id == ledgerId)
            .Join(db.Companies, l => l.CompanyId, c => c.Id, (l, c) => new { l, c }).SingleAsync(ct);
        return CompanyService.ToDto(row.l, row.c, LedgerAccess.Edit);
    }
}
