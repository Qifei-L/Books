using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record ReturnBoxDto(string Key, string Section, string Label, string Kind, bool Information);

public sealed record TaxPackDto(string TaxPack, string ReturnName, string? TaxCurrency, IReadOnlyList<WithholdingCategory> WithholdingCategories, IReadOnlyList<ReturnBoxDto> Boxes);

/// <summary>
/// The monthly tax return of a ledger's tax pack. The pack defines the boxes; the return is calculated from posted
/// documents, so people and AI clients (through MCP) read the same definition and the same figures.
/// </summary>
public sealed class TaxReturnService(IBooksDb db, ReadRunner reads)
{
    public Task<TaxPackDto> PackAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            var definition = Definition(ledger.TaxPack);
            return new TaxPackDto(definition.TaxPack, definition.Name, TaxPacks.TaxCurrency(ledger.TaxPack)?.Currency, definition.WithholdingCategories,
                definition.Boxes.Select(b => new ReturnBoxDto(b.Key, b.Section, b.Label, b.Formula is null ? "lines" : "formula", b.Information)).ToList());
        }, ct);

    private static TaxReturnDefinition Definition(string? taxPack) =>
        TaxReturns.For(taxPack) ?? throw BooksProblem.Conflict("NO_TAX_PACK", "This ledger has no tax pack; apply the Cambodia template first.");
}
