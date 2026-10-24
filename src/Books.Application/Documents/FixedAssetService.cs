using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <param name="AssetAccountId">Omit for the ledger's only account with the fixed_asset usage; likewise the other two.</param>
public sealed record FixedAssetInput(
    string Code, string Name, DateOnly AcquisitionDate, decimal Cost, decimal ResidualValue, int UsefulLifeMonths,
    Guid? AssetAccountId = null, Guid? AccumulatedDepreciationAccountId = null, Guid? DepreciationExpenseAccountId = null);

public sealed record FixedAssetDto(
    Guid Id, string Code, string Name, DateOnly AcquisitionDate, string Cost, string ResidualValue, int UsefulLifeMonths, string Monthly,
    Guid AssetAccountId, Guid AccumulatedDepreciationAccountId, Guid DepreciationExpenseAccountId,
    string Accumulated, string NetBookValue, DateOnly? DisposedOn, Guid? DisposalJournalId, string Version);

/// <param name="MatchesControl">Register cost equals the fixed asset accounts and accumulated depreciation equals its accounts.</param>
public sealed record FixedAssetRegister(
    DateOnly AsOf, string Currency, IReadOnlyList<FixedAssetDto> Assets, string Cost, string Accumulated, string NetBookValue,
    string AssetAccountsBalance, string AccumulatedAccountsBalance, bool MatchesControl);

public sealed record DepreciationRunDto(Guid Id, string Period, string Status, Guid? JournalId, IReadOnlyList<DepreciationRunLine> Lines, string Total);

public sealed record DepreciationRunLine(Guid AssetId, string Code, string Name, string Amount);

/// <summary>
/// The fixed asset register and its depreciation (M10, owner default: straight line, monthly, from the month after
/// acquisition). Anyone who edits the ledger keeps the register, AI clients included; posting depreciation and
/// disposals is for people, like every posting.
/// </summary>
public sealed class FixedAssetService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting)
{
    private sealed record Total(decimal Amount);

    public Task<FixedAssetRegister> RegisterAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly asOf, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var currency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
            var minor = Currencies.Require(currency).MinorUnits;
            string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
            var assets = await db.FixedAssets.AsNoTracking().Where(a => a.LedgerId == ledgerId && a.AcquisitionDate <= asOf).OrderBy(a => a.Code).ToListAsync(ct);
            var accumulated = await AccumulatedAsync(ledgerId, asOf, ct);
            var live = assets.Where(a => a.DisposedOn is not { } d || d > asOf).ToList();
            var rows = assets.Select(a => ToDto(a, accumulated.GetValueOrDefault(a.Id), minor)).ToList();
            var cost = live.Sum(a => a.Cost);
            var acc = live.Sum(a => accumulated.GetValueOrDefault(a.Id));
            var assetBalance = await BalanceAsync(ledgerId, "fixed_asset", asOf, ct);
            var accBalance = -await BalanceAsync(ledgerId, "accumulated_depreciation", asOf, ct);
            return new FixedAssetRegister(asOf, currency, rows, F(cost), F(acc), F(cost - acc), F(assetBalance), F(accBalance), assetBalance == cost && accBalance == acc);
        }, ct);

    public Task<WriteResult> SaveAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid? assetId, FixedAssetInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(assetId is null ? "fixed_asset.create" : "fixed_asset.update", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var asset = assetId is { } id
                ? await db.FixedAssets.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == id, ct) ?? throw BooksProblem.NotFound()
                : new FixedAsset(Guid.CreateVersion7(), orgId, ledgerId, scope.Now);
            if (assetId is not null)
            {
                scope.CheckVersion(asset.Version);
                if (await db.DepreciationLines.AnyAsync(l => l.AssetId == asset.Id && db.DepreciationRuns.Any(r => r.Id == l.RunId && r.Status == DocumentStatus.Posted), ct)
                    && (input.Cost != asset.Cost || input.ResidualValue != asset.ResidualValue || input.UsefulLifeMonths != asset.UsefulLifeMonths))
                {
                    throw BooksProblem.Conflict("ASSET_DEPRECIATED", "Depreciation is posted for this asset; delete those runs before changing cost, residual value or life.");
                }
            }
            try
            {
                asset.Set(input.Code, input.Name, input.AcquisitionDate, input.Cost, input.ResidualValue, input.UsefulLifeMonths,
                    await AccountAsync(ledgerId, Usage.FixedAsset, input.AssetAccountId, ct),
                    await AccountAsync(ledgerId, Usage.AccumulatedDepreciation, input.AccumulatedDepreciationAccountId, ct),
                    await AccountAsync(ledgerId, Usage.DepreciationExpense, input.DepreciationExpenseAccountId, ct), isNew: assetId is null);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            if (assetId is null)
            {
                db.FixedAssets.Add(asset);
            }
            scope.Audit(assetId is null ? "fixed_asset.created" : "fixed_asset.updated", "fixed_asset", asset.Id, new { asset.Code, asset.Name, asset.Cost });
            await db.SaveChangesAsync(ct);
            var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
            return WriteResult.Of(assetId is null ? 201 : 200, ToDto(asset, (await AccumulatedAsync(ledgerId, DateOnly.MaxValue, ct)).GetValueOrDefault(asset.Id), minor), asset.Version);
        }, ct);

    /// <summary>Posts a month's straight-line depreciation for every asset acquired before the month and not yet disposed.</summary>
    public Task<WriteResult> RunAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, DateOnly period, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("depreciation.run", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            JournalService.RefuseAgentPosting(principal, touchesPosting: true);
            if (period.Day != 1)
            {
                throw BooksProblem.Invalid("period must be a month.");
            }
            if (await db.DepreciationRuns.AnyAsync(r => r.LedgerId == ledgerId && r.Period == period && r.Status != DocumentStatus.Deleted, ct))
            {
                throw BooksProblem.Conflict("DEPRECIATION_ALREADY_RUN", $"Depreciation for {period:yyyy-MM} is already posted; delete that run to redo it.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
            var monthEnd = period.AddMonths(1).AddDays(-1);
            var assets = await db.FixedAssets.Where(a => a.LedgerId == ledgerId && a.AcquisitionDate < period && (a.DisposedOn == null || a.DisposedOn > monthEnd)).ToListAsync(ct);
            var done = await AccumulatedAsync(ledgerId, DateOnly.MaxValue, ct);
            var lines = assets.Select(a => new DepreciationLine(a.Id,
                    Math.Min(Math.Round(a.Monthly, minor, MidpointRounding.AwayFromZero), a.Cost - a.ResidualValue - done.GetValueOrDefault(a.Id))))
                .Where(l => l.Amount > 0).ToList();
            if (lines.Count == 0)
            {
                throw BooksProblem.Conflict("NOTHING_TO_DEPRECIATE", $"No asset has depreciation left for {period:yyyy-MM}.");
            }
            var run = new DepreciationRun(Guid.CreateVersion7(), orgId, ledgerId, period, lines, scope.Now);
            db.DepreciationRuns.Add(run);
            var outcome = await posting.PostAsync(scope, ledger, DepreciationRun.Kind, run.Id, null, run.ToJournal(assets.ToDictionary(a => a.Id)), ct);
            if (outcome.Journal is not { } journal)
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The depreciation cannot be posted.") { Issues = outcome.Issues };
            }
            run.PostedAs(journal.Id);
            scope.Audit("depreciation.posted", "ledger", ledgerId, new { period, assets = lines.Count, total = lines.Sum(l => l.Amount) });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, await RunDtoAsync(run, minor, ct));
        }, ct);

    public Task<IReadOnlyList<DepreciationRunDto>> RunsAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<DepreciationRunDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
            var runs = await db.DepreciationRuns.AsNoTracking().Include(r => r.Lines).Where(r => r.LedgerId == ledgerId).OrderByDescending(r => r.Period).ToListAsync(ct);
            var result = new List<DepreciationRunDto>();
            foreach (var run in runs)
            {
                result.Add(await RunDtoAsync(run, minor, ct));
            }
            return result;
        }, ct);

    public Task<WriteResult> DeleteRunAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid runId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("depreciation.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var run = await db.DepreciationRuns.SingleOrDefaultAsync(r => r.LedgerId == ledgerId && r.Id == runId && r.Status == DocumentStatus.Posted, ct) ?? throw BooksProblem.NotFound();
            await posting.DeleteAsync(scope, await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct), run.JournalId!.Value, ct);
            run.Delete();
            scope.Audit("depreciation.deleted", "ledger", ledgerId, new { period = run.Period });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, new { id = run.Id, deleted = true });
        }, ct);

    /// <summary>Takes the asset off the books on the date (people only); proceeds post separately to the same gain or loss account.</summary>
    public Task<WriteResult> DisposeAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid assetId, DateOnly date, Guid gainOrLossAccountId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("fixed_asset.dispose", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            JournalService.RefuseAgentPosting(principal, touchesPosting: true);
            var asset = await db.FixedAssets.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == assetId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(asset.Version);
            if (asset.DisposedOn is not null || date < asset.AcquisitionDate)
            {
                throw BooksProblem.Invalid("The asset is already disposed, or the date is before its acquisition.");
            }
            if (await db.DepreciationRuns.AnyAsync(r => r.LedgerId == ledgerId && r.Status == DocumentStatus.Posted && r.Period > date && r.Lines.Any(l => l.AssetId == asset.Id), ct))
            {
                throw BooksProblem.Conflict("ASSET_DEPRECIATED", "Depreciation after the disposal date is posted for this asset; delete those runs first.");
            }
            if (!await db.Accounts.AnyAsync(a => a.LedgerId == ledgerId && a.Id == gainOrLossAccountId && !a.IsControl, ct))
            {
                throw BooksProblem.Invalid("gain_or_loss_account_id must be an ordinary account of this ledger.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var accumulated = (await AccumulatedAsync(ledgerId, date, ct)).GetValueOrDefault(asset.Id);
            var outcome = await posting.PostAsync(scope, ledger, FixedAsset.DisposalKind, asset.Id, null, asset.DisposalJournal(date, accumulated, gainOrLossAccountId), ct);
            if (outcome.Journal is not { } journal)
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The disposal cannot be posted.") { Issues = outcome.Issues };
            }
            asset.Disposed(date, journal.Id);
            scope.Audit("fixed_asset.disposed", "fixed_asset", asset.Id, new { date, accumulated });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, ToDto(asset, accumulated, Currencies.Require(ledger.BaseCurrency).MinorUnits), asset.Version);
        }, ct);

    private async Task<Guid> AccountAsync(Guid ledgerId, Usage usage, Guid? given, CancellationToken ct)
    {
        var accounts = await db.AccountUsages.Where(u => u.LedgerId == ledgerId && u.Usage == usage).Select(u => u.AccountId).ToListAsync(ct);
        if (given is { } id)
        {
            return accounts.Contains(id) ? id : throw BooksProblem.Invalid($"The account must carry the {Text.Of(usage)} usage.");
        }
        return accounts.Count == 1 ? accounts[0] : throw BooksProblem.Invalid($"Give the account: the ledger needs exactly one account with the {Text.Of(usage)} usage to choose it.");
    }

    /// <summary>Posted depreciation per asset whose journal (dated the month end) is on or before the date.</summary>
    private async Task<Dictionary<Guid, decimal>> AccumulatedAsync(Guid ledgerId, DateOnly asOf, CancellationToken ct)
    {
        var next = asOf == DateOnly.MaxValue ? asOf : asOf.AddDays(1);
        var before = asOf == DateOnly.MaxValue ? DateOnly.MaxValue : new DateOnly(next.Year, next.Month, 1);
        return (await db.DepreciationRuns.AsNoTracking().Where(r => r.LedgerId == ledgerId && r.Status == DocumentStatus.Posted && r.Period < before)
            .SelectMany(r => r.Lines).ToListAsync(ct))
            .GroupBy(l => l.AssetId).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
    }

    private async Task<decimal> BalanceAsync(Guid ledgerId, string usage, DateOnly asOf, CancellationToken ct) =>
        (await db.Database.SqlQuery<Total>($"""
            SELECT COALESCE(SUM(l.amount), 0) AS amount
            FROM journals j
            JOIN journal_revisions r ON r.id = j.effective_revision_id
            JOIN journal_lines l ON l.revision_id = r.id
            JOIN account_usages u ON u.account_id = l.account_id AND u.usage = {usage}
            WHERE j.ledger_id = {ledgerId} AND r.accounting_date <= {asOf}
            """).SingleAsync(ct)).Amount;

    private async Task<DepreciationRunDto> RunDtoAsync(DepreciationRun run, int minor, CancellationToken ct)
    {
        var ids = run.Lines.Select(l => l.AssetId).ToList();
        var assets = await db.FixedAssets.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        return new DepreciationRunDto(run.Id, run.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture), Text.Of(run.Status), run.JournalId,
            run.Lines.OrderBy(l => l.LineNo).Select(l => new DepreciationRunLine(l.AssetId, assets[l.AssetId].Code, assets[l.AssetId].Name, F(l.Amount))).ToList(),
            F(run.Lines.Sum(l => l.Amount)));
    }

    private static FixedAssetDto ToDto(FixedAsset a, decimal accumulated, int minor)
    {
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        return new FixedAssetDto(a.Id, a.Code, a.Name, a.AcquisitionDate, F(a.Cost), F(a.ResidualValue), a.UsefulLifeMonths,
            F(Math.Round(a.Monthly, minor, MidpointRounding.AwayFromZero)), a.AssetAccountId, a.AccumulatedDepreciationAccountId, a.DepreciationExpenseAccountId,
            F(accumulated), F(a.Cost - accumulated), a.DisposedOn, a.DisposalJournalId, a.Version.ToString(CultureInfo.InvariantCulture));
    }
}
