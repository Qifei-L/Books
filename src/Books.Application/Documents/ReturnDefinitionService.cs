using System.Globalization;
using System.Text.Json;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <param name="Number">0 is the tax pack's built-in definition.</param>
/// <param name="EffectiveFrom">yyyy-MM; null for the built-in definition, which applies until a published version starts.</param>
/// <param name="Version">Concurrency version for If-Match (drafts only change).</param>
public sealed record ReturnDefinitionDto(
    int Number, string Status, string? EffectiveFrom, ReturnDefinitionSpec Definition,
    Guid? CreatedBy, DateTimeOffset? CreatedAt, Guid? PublishedBy, DateTimeOffset? PublishedAt, string? Version);

/// <summary>
/// Versions of a ledger's tax return definition (owner decision): anyone who can edit the ledger, including AI
/// clients, drafts a version; administrators publish it, after which it never changes and applies from its month.
/// Months already filed keep the version they were filed with.
/// </summary>
public sealed class ReturnDefinitionService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<ReturnDefinitionDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<ReturnDefinitionDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            var builtin = Builtin(ledger);
            var rows = await db.ReturnDefinitionVersions.AsNoTracking().Where(v => v.LedgerId == ledgerId).OrderBy(v => v.VersionNo).ToListAsync(ct);
            return [BuiltinDto(builtin), .. rows.Select(ToDto)];
        }, ct);

    public Task<ReturnDefinitionDto> GetAsync(Principal principal, Guid orgId, Guid ledgerId, int number, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            return number == 0 ? BuiltinDto(Builtin(ledger))
                : ToDto(await db.ReturnDefinitionVersions.AsNoTracking().SingleOrDefaultAsync(v => v.LedgerId == ledgerId && v.VersionNo == number, ct)
                    ?? throw BooksProblem.NotFound());
        }, ct);

    public Task<WriteResult> CreateDraftAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, string effectiveFrom, ReturnDefinitionSpec definition, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_return_definition.draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            Builtin(ledger);
            var month = Month(effectiveFrom);
            Check(ledger, month, definition);
            var number = (await db.ReturnDefinitionVersions.Where(v => v.LedgerId == ledgerId).MaxAsync(v => (int?)v.VersionNo, ct) ?? 0) + 1;
            var version = new ReturnDefinitionVersion(Guid.CreateVersion7(), orgId, ledgerId, number, month, BooksJson.Serialize(definition), principal.UserId, scope.Now);
            db.ReturnDefinitionVersions.Add(version);
            scope.Audit("tax_return_definition.drafted", "ledger", ledgerId, new { number, effective_from = effectiveFrom });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, ToDto(version), version.Version);
        }, ct);

    public Task<WriteResult> UpdateDraftAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, int number, string effectiveFrom, ReturnDefinitionSpec definition, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_return_definition.update", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var version = await FindAsync(ledgerId, number, ct);
            scope.CheckVersion(version.Version);
            var month = Month(effectiveFrom);
            Check(ledger, month, definition);
            version.Update(month, BooksJson.Serialize(definition));
            scope.Audit("tax_return_definition.updated", "ledger", ledgerId, new { number, effective_from = effectiveFrom });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, ToDto(version), version.Version);
        }, ct);

    /// <summary>Administrators only, people only. A version cannot start in or before a month already filed.</summary>
    public Task<WriteResult> PublishAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, int number, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_return_definition.publish", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            JournalService.RefuseAgentPosting(principal, touchesPosting: true);
            var version = await FindAsync(ledgerId, number, ct);
            scope.CheckVersion(version.Version);
            if (await db.FiledTaxReturns.AnyAsync(f => f.LedgerId == ledgerId && f.Period >= version.EffectiveFrom, ct))
            {
                throw BooksProblem.Conflict("MONTH_FILED", "A month on or after the effective month is already filed; start the version after the last filed month.");
            }
            if (await db.ReturnDefinitionVersions.AnyAsync(v => v.LedgerId == ledgerId && v.Status == DefinitionStatus.Published && v.EffectiveFrom == version.EffectiveFrom, ct))
            {
                throw BooksProblem.Conflict("EFFECTIVE_MONTH_TAKEN", "Another published version starts in the same month.");
            }
            version.Publish(principal.UserId, scope.Now);
            scope.Audit("tax_return_definition.published", "ledger", ledgerId, new { number, effective_from = version.EffectiveFrom });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, ToDto(version), version.Version);
        }, ct);

    /// <summary>
    /// The definition for a month: the latest published version starting on or before it, else the built-in one.
    /// A number picks a specific version instead, for previewing a draft.
    /// </summary>
    internal async Task<TaxReturnDefinition> EffectiveAsync(Ledger ledger, DateOnly month, int? number, CancellationToken ct)
    {
        var builtin = Builtin(ledger);
        var row = number switch
        {
            0 => null,
            { } n => await db.ReturnDefinitionVersions.AsNoTracking().SingleOrDefaultAsync(v => v.LedgerId == ledger.Id && v.VersionNo == n, ct)
                ?? throw BooksProblem.Invalid($"Definition version {n} does not exist."),
            null => await db.ReturnDefinitionVersions.AsNoTracking()
                .Where(v => v.LedgerId == ledger.Id && v.Status == DefinitionStatus.Published && v.EffectiveFrom <= month)
                .OrderByDescending(v => v.EffectiveFrom).FirstOrDefaultAsync(ct),
        };
        return row is null
            ? TaxReturns.Compile(ledger.TaxPack!, 0, null, builtin)
            : TaxReturns.Compile(ledger.TaxPack!, row.VersionNo, row.EffectiveFrom, Spec(row));
    }

    /// <summary>Withholding categories a tax code may use: those of the latest published version, else the built-in ones.</summary>
    /// <summary>The return categories a tax code of this kind may use: withholding categories, or VAT categories such as imports.</summary>
    internal async Task<IReadOnlyCollection<string>> CategoriesAsync(Ledger ledger, TaxKind kind, CancellationToken ct)
    {
        if (TaxReturns.Builtin(ledger.TaxPack) is not { } builtin)
        {
            return [];
        }
        var latest = await db.ReturnDefinitionVersions.AsNoTracking()
            .Where(v => v.LedgerId == ledger.Id && v.Status == DefinitionStatus.Published).OrderByDescending(v => v.EffectiveFrom).FirstOrDefaultAsync(ct);
        var spec = latest is null ? builtin : Spec(latest);
        var kindName = Text.Of(kind);
        return kind == TaxKind.Withholding
            ? spec.WithholdingCategories.Select(c => c.Key).ToList()
            : (spec.TaxCategories ?? []).Where(c => c.Kind == kindName).Select(c => c.Key).ToList();
    }

    private static void Check(Ledger ledger, DateOnly month, ReturnDefinitionSpec definition)
    {
        try
        {
            TaxReturns.Compile(ledger.TaxPack!, 0, month, definition);
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }

    private async Task<ReturnDefinitionVersion> FindAsync(Guid ledgerId, int number, CancellationToken ct) =>
        await db.ReturnDefinitionVersions.SingleOrDefaultAsync(v => v.LedgerId == ledgerId && v.VersionNo == number, ct)
            ?? throw BooksProblem.NotFound();

    private static ReturnDefinitionSpec Builtin(Ledger ledger) =>
        TaxReturns.Builtin(ledger.TaxPack) ?? throw BooksProblem.Conflict("NO_TAX_PACK", "This ledger has no tax pack; apply the Cambodia template first.");

    private static DateOnly Month(string value) =>
        DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month)
            ? month
            : throw BooksProblem.Invalid("effective_from must be a month, yyyy-MM.");

    private static ReturnDefinitionSpec Spec(ReturnDefinitionVersion v) => JsonSerializer.Deserialize<ReturnDefinitionSpec>(v.Definition, BooksJson.Options)!;

    private static ReturnDefinitionDto BuiltinDto(ReturnDefinitionSpec spec) => new(0, "built_in", null, spec, null, null, null, null, null);

    private static ReturnDefinitionDto ToDto(ReturnDefinitionVersion v) => new(
        v.VersionNo, Text.Of(v.Status), v.EffectiveFrom.ToString("yyyy-MM", CultureInfo.InvariantCulture), Spec(v),
        v.CreatedBy, v.CreatedAt, v.PublishedBy, v.PublishedAt, v.Version.ToString(CultureInfo.InvariantCulture));
}
