using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record TaxCodeDto(
    Guid Id, Guid LedgerId, string Name, string Kind, string Rate, Guid? AccountId,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool Active, string Version);

public sealed record TaxCodeInput(string Name, TaxKind Kind, decimal Rate, Guid? AccountId, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool Active = true);

/// <summary>Tax codes of a ledger. Anyone with access reads them; only administrators change them (owner decision).</summary>
public sealed class TaxCodeService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<TaxCodeDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, TaxCodeInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_code.create", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var code = new TaxCode(Guid.CreateVersion7(), ledger, input.Name, input.Kind, input.Rate,
                await AccountAsync(ledgerId, input.AccountId, ct), input.EffectiveFrom, input.EffectiveTo, scope.Now);
            db.TaxCodes.Add(code);
            await db.SaveChangesAsync(ct);
            var dto = (await LoadAsync(ledgerId, code.Id, ct)).Single();
            scope.Audit("tax_code.created", "tax_code", code.Id, dto);
            return WriteResult.Of(201, dto, code.Version);
        }, ct);

    /// <summary>Replaces name, rate, account, dates and active flag; the kind never changes.</summary>
    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid taxCodeId, TaxCodeInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_code.update", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            var code = await db.TaxCodes.SingleOrDefaultAsync(t => t.LedgerId == ledgerId && t.Id == taxCodeId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(code.Version);
            if (input.Kind != code.Kind)
            {
                throw BooksProblem.Invalid("The kind of a tax code cannot change; create a new code instead.");
            }
            var before = (await LoadAsync(ledgerId, code.Id, ct)).Single();
            code.Update(input.Name, input.Rate, await AccountAsync(ledgerId, input.AccountId, ct), input.EffectiveFrom, input.EffectiveTo, input.Active);
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, code.Id, ct)).Single();
            scope.Audit("tax_code.updated", "tax_code", code.Id, new { before, after });
            return WriteResult.Of(200, after, code.Version);
        }, ct);

    private async Task<Account?> AccountAsync(Guid ledgerId, Guid? accountId, CancellationToken ct) =>
        accountId is { } id
            ? await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == id, ct)
                ?? throw BooksProblem.Invalid("account_id must be an account in this ledger.")
            : null;

    private async Task<IReadOnlyList<TaxCodeDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct) =>
        (await db.TaxCodes.AsNoTracking().Where(t => t.LedgerId == ledgerId && (id == null || t.Id == id))
            .OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(ct))
        .Select(t => new TaxCodeDto(t.Id, t.LedgerId, t.Name, Text.Of(t.Kind), t.Rate.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            t.AccountId, t.EffectiveFrom, t.EffectiveTo, t.Active, t.Version.ToString()))
        .ToList();
}
