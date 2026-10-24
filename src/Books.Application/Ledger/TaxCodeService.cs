using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record TaxCodeDto(
    Guid Id, Guid LedgerId, string Name, string Kind, string Rate, Guid? AccountId, Guid? NonDeductibleAccountId,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, string? ReturnCategory, bool Active, string Version);

public sealed record TaxCodeInput(
    string Name, TaxKind Kind, decimal Rate, Guid? AccountId, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool Active = true, Guid? NonDeductibleAccountId = null, string? ReturnCategory = null);

/// <summary>Tax codes of a ledger. Anyone with access reads them; only administrators change them (owner decision).</summary>
public sealed class TaxCodeService(IBooksDb db, WriteRunner writes, ReadRunner reads, AccountUsageService usages)
{
    public Task<IReadOnlyList<TaxCodeDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, TaxCodeInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_code.create", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var (account, nonDeductible) = (await AccountAsync(ledgerId, input.AccountId, ct), await AccountAsync(ledgerId, input.NonDeductibleAccountId, ct));
            var code = new TaxCode(Guid.CreateVersion7(), ledger, input.Name, input.Kind, input.Rate, account, nonDeductible, input.EffectiveFrom, input.EffectiveTo, scope.Now);
            code.SetReturnCategory(input.ReturnCategory, Categories(ledger));
            await EnsureUsagesAsync(code, account, nonDeductible, ct);
            db.TaxCodes.Add(code);
            await db.SaveChangesAsync(ct);
            var dto = (await LoadAsync(ledgerId, code.Id, ct)).Single();
            scope.Audit("tax_code.created", "tax_code", code.Id, dto);
            return WriteResult.Of(201, dto, code.Version);
        }, ct);

    /// <summary>Replaces name, rate, account, dates and active flag; the kind never changes.</summary>
    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid taxCodeId, TaxCodeInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_code.update", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            var code = await db.TaxCodes.SingleOrDefaultAsync(t => t.LedgerId == ledgerId && t.Id == taxCodeId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(code.Version);
            if (input.Kind != code.Kind)
            {
                throw BooksProblem.Invalid("The kind of a tax code cannot change; create a new code instead.");
            }
            var before = (await LoadAsync(ledgerId, code.Id, ct)).Single();
            var (account, nonDeductible) = (await AccountAsync(ledgerId, input.AccountId, ct), await AccountAsync(ledgerId, input.NonDeductibleAccountId, ct));
            code.Update(input.Name, input.Rate, account, nonDeductible, input.EffectiveFrom, input.EffectiveTo, input.Active);
            code.SetReturnCategory(input.ReturnCategory, Categories(await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct)));
            await EnsureUsagesAsync(code, account, nonDeductible, ct);
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, code.Id, ct)).Single();
            scope.Audit("tax_code.updated", "tax_code", code.Id, new { before, after });
            return WriteResult.Of(200, after, code.Version);
        }, ct);

    internal static IReadOnlyCollection<string> Categories(Ledger ledger) =>
        TaxReturns.For(ledger.TaxPack)?.WithholdingCategories.Select(c => c.Key).ToList() ?? [];

    /// <summary>The referenced accounts carry the matching usage, added here in the same transaction if missing.</summary>
    private async Task EnsureUsagesAsync(TaxCode code, Account? account, Account? nonDeductible, CancellationToken ct)
    {
        if (account is not null && Usages.ForTaxKind(code.Kind) is { } usage)
        {
            await usages.EnsureAsync(account, usage, ct);
        }
        if (nonDeductible is not null)
        {
            await usages.EnsureAsync(nonDeductible, Usage.NonDeductibleVat, ct);
        }
    }

    private async Task<Account?> AccountAsync(Guid ledgerId, Guid? accountId, CancellationToken ct) =>
        accountId is { } id
            ? await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == id, ct)
                ?? throw BooksProblem.Invalid("account_id must be an account in this ledger.")
            : null;

    private async Task<IReadOnlyList<TaxCodeDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct) =>
        (await db.TaxCodes.AsNoTracking().Where(t => t.LedgerId == ledgerId && (id == null || t.Id == id))
            .OrderBy(t => t.Name).ThenBy(t => t.Id).ToListAsync(ct))
        .Select(t => new TaxCodeDto(t.Id, t.LedgerId, t.Name, Text.Of(t.Kind), t.Rate.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            t.AccountId, t.NonDeductibleAccountId, t.EffectiveFrom, t.EffectiveTo, t.ReturnCategory, t.Active, t.Version.ToString()))
        .ToList();
}
