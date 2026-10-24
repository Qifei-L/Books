using System.Globalization;
using System.Text.Json;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Reporting;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record FxRevaluationLineDto(
    string Kind, Guid AccountId, Guid? PartyId, Guid? InvoiceId, string? Description, string Currency, string ForeignAmount, string Before, string After, string Adjustment);

/// <param name="Rates">Closing rates used: units of each currency per one unit of the ledger's.</param>
public sealed record FxRevaluationDto(
    Guid? Id, long? RevaluationNo, DateOnly Date, IReadOnlyDictionary<string, string> Rates, IReadOnlyList<FxRevaluationLineDto> Lines, string Total,
    string Status, Guid? JournalId, Guid? ReversalJournalId, string? Version);

/// <summary>
/// Year-end revaluation (M12.2b). The preview shows what would post; creating posts the revaluation on the date and
/// its reversal the day after, together; deleting removes both. Revalue before closing the year.
/// </summary>
public sealed class FxRevaluationService(
    IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting, ReceivablesReportService receivables, PayablesReportService payables)
{
    private sealed record Balance(Guid AccountId, decimal Amount, decimal CurrencyAmount);

    public Task<IReadOnlyList<FxRevaluationDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<FxRevaluationDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            return (await db.FxRevaluations.AsNoTracking().Include(r => r.Lines).Where(r => r.LedgerId == ledgerId)
                    .OrderByDescending(r => r.Date).ThenByDescending(r => r.RevaluationNo).ToListAsync(ct))
                .Select(r => ToDto(r, ledger)).ToList();
        }, ct);

    /// <summary>What a revaluation on the date would post, without posting it.</summary>
    public Task<FxRevaluationDto> PreviewAsync(Principal principal, Guid orgId, Guid ledgerId, DateOnly date, IReadOnlyDictionary<string, decimal>? rates, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            var (lines, used) = await LinesAsync(ledger, date, rates, ct);
            var baseMinor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
            return new FxRevaluationDto(null, null, date, Format(used), lines.Select(l => ToDto(l, baseMinor)).ToList(),
                F(lines.Sum(l => l.Adjustment), baseMinor), "preview", null, null, null);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, DateOnly date, IReadOnlyDictionary<string, decimal>? rates, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("fx_revaluation.post", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            if (await db.FxRevaluations.AnyAsync(r => r.LedgerId == ledgerId && r.Date == date && r.Status != DocumentStatus.Deleted, ct))
            {
                throw BooksProblem.Conflict("REVALUATION_EXISTS", $"Balances are already revalued at {date:yyyy-MM-dd}; delete that revaluation to redo it.");
            }
            var (lines, used) = await LinesAsync(ledger, date, rates, ct);
            FxRevaluation revaluation;
            try
            {
                revaluation = new FxRevaluation(Guid.CreateVersion7(), orgId, ledgerId, await posting.NextNumberAsync(ledgerId, FxRevaluation.Kind, ct), date,
                    JsonSerializer.Serialize(Format(used)), lines, principal.UserId, scope.Now);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            var unrealized = await Inventory.AccountAsync(db, ledgerId, Usage.FxUnrealized, ct);
            var posted = await posting.PostAsync(scope, ledger, FxRevaluation.Kind, revaluation.Id, null, revaluation.ToJournal(unrealized), ct);
            var reversed = posted.Journal is null ? posted
                : await posting.PostAsync(scope, ledger, FxRevaluation.ReversalKind, revaluation.Id, null, revaluation.ToReversal(unrealized), ct);
            if (posted.Journal is null || reversed.Journal is null)
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The revaluation cannot be posted.") { Issues = (posted.Journal is null ? posted : reversed).Issues.ToList() };
            }
            revaluation.PostedAs(posted.Journal.Id, reversed.Journal.Id);
            db.FxRevaluations.Add(revaluation);
            scope.Audit("fx_revaluation.posted", FxRevaluation.Kind, revaluation.Id, new { revaluation.RevaluationNo, date, rates = revaluation.Rates });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, ToDto(revaluation, ledger), revaluation.Version);
        }, ct);

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid revaluationId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("fx_revaluation.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var revaluation = await db.FxRevaluations.SingleOrDefaultAsync(r => r.LedgerId == ledgerId && r.Id == revaluationId, ct) ?? throw BooksProblem.NotFound();
            if (revaluation.Status == DocumentStatus.Deleted)
            {
                throw BooksProblem.Conflict("DOCUMENT_DELETED", "This revaluation was deleted.");
            }
            scope.CheckVersion(revaluation.Version);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            await posting.DeleteAsync(scope, ledger, revaluation.JournalId!.Value, ct);
            await posting.DeleteAsync(scope, ledger, revaluation.ReversalJournalId!.Value, ct);
            revaluation.Delete();
            scope.Audit("fx_revaluation.deleted", FxRevaluation.Kind, revaluation.Id, new { revaluation.RevaluationNo });
            return WriteResult.Of(200, new { id = revaluation.Id, deleted = true }, revaluation.Version);
        }, ct);

    /// <summary>
    /// Whether balances in another currency would change at the date and no revaluation is posted for it. A missing
    /// closing rate counts as needing one when there are such balances.
    /// </summary>
    internal async Task<bool> NeedsRevaluationAsync(Ledger ledger, DateOnly date, CancellationToken ct)
    {
        if (await db.FxRevaluations.AnyAsync(r => r.LedgerId == ledger.Id && r.Date == date && r.Status != DocumentStatus.Deleted, ct))
        {
            return false;
        }
        try
        {
            return (await LinesAsync(ledger, date, null, ct)).Lines.Count > 0;
        }
        catch (BooksProblem)
        {
            return true;
        }
    }

    /// <summary>
    /// The lines at the closing rates: each bank account in another currency, and each open invoice in another
    /// currency. An invoice's open amount in its own currency is its open value at its own rate (settlements leave
    /// the control account at the invoice's rate, so the two stay in step); payables are credits, so negative.
    /// </summary>
    private async Task<(List<FxRevaluationLine> Lines, Dictionary<string, decimal> Rates)> LinesAsync(
        Ledger ledger, DateOnly date, IReadOnlyDictionary<string, decimal>? given, CancellationToken ct)
    {
        var baseMinor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var used = new Dictionary<string, decimal>();
        async Task<decimal> RateAsync(string currency)
        {
            if (!used.TryGetValue(currency, out var rate))
            {
                rate = (await Rates.ForDocumentAsync(db, ledger, currency, given?.GetValueOrDefault(currency) is { } r and > 0 ? r : null, date, ct))!.Value;
                used[currency] = rate;
            }
            return rate;
        }
        decimal At(decimal foreign, decimal rate) => Math.Round(foreign / rate, baseMinor, MidpointRounding.AwayFromZero);
        var lines = new List<FxRevaluationLine>();

        var banks = await db.BankAccounts.AsNoTracking().Where(b => b.LedgerId == ledger.Id && b.Currency != ledger.BaseCurrency).ToListAsync(ct);
        if (banks.Count > 0)
        {
            var ids = banks.Select(b => b.AccountId).ToList();
            var balances = (await db.Database.SqlQuery<Balance>($"""
                SELECT l.account_id AS account_id, SUM(l.amount) AS amount, SUM(COALESCE(l.currency_amount, 0)) AS currency_amount
                FROM journals j
                JOIN journal_revisions r ON r.id = j.effective_revision_id
                JOIN journal_lines l ON l.revision_id = r.id
                WHERE j.ledger_id = {ledger.Id} AND r.accounting_date <= {date} AND l.account_id = ANY({ids})
                GROUP BY l.account_id
                """).ToListAsync(ct)).ToDictionary(b => b.AccountId);
            var names = await db.Accounts.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => $"{a.Code} {a.Name}", ct);
            foreach (var bank in banks.Where(b => balances.ContainsKey(b.AccountId)))
            {
                var balance = balances[bank.AccountId];
                var after = At(balance.CurrencyAmount, await RateAsync(bank.Currency));
                if (after != balance.Amount)
                {
                    lines.Add(new FxRevaluationLine(RevaluationLineKind.Bank, bank.AccountId, null, null, names[bank.AccountId], bank.Currency,
                        balance.CurrencyAmount, balance.Amount, after));
                }
            }
        }

        // Open invoices in another currency, and credits left over from settling one (overpayments, owner decision).
        async Task InvoicesAsync(RevaluationLineKind kind, IEnumerable<OpenItem> open, Dictionary<Guid, (string Currency, decimal Rate, string Label)> foreign, Usage usage)
        {
            Guid? account = null;
            var sign = kind == RevaluationLineKind.Receivable ? 1 : -1;
            foreach (var item in open.Where(i => i.Currency is not null && i.Rate is not null && i.Currency != ledger.BaseCurrency && i.Amount != 0
                         && i.Date <= date).OrderBy(i => i.Date).ThenBy(i => i.Number))
            {
                var currency = item.Currency!;
                var rate = item.Rate!.Value;
                var label = item.InvoiceId is { } id && foreign.TryGetValue(id, out var invoice) ? invoice.Label : $"Credit balance in {currency}";
                var own = Math.Round(item.Amount * rate, Currencies.Require(currency).MinorUnits, MidpointRounding.AwayFromZero);
                var after = At(own, await RateAsync(currency));
                if (after != item.Amount)
                {
                    account ??= await Inventory.AccountAsync(db, ledger.Id, usage, ct);
                    lines.Add(new FxRevaluationLine(kind, account.Value, item.PartyId, item.InvoiceId, label, currency, sign * own, sign * item.Amount, sign * after));
                }
            }
        }

        var sales = await db.SalesInvoices.AsNoTracking().Where(i => i.LedgerId == ledger.Id && i.Status == DocumentStatus.Posted && !i.IsCreditNote
                && i.Currency != "" && i.Currency != ledger.BaseCurrency && i.ExchangeRate != null && i.Date <= date)
            .ToDictionaryAsync(i => i.Id, i => (i.Currency, i.ExchangeRate!.Value, $"Sales invoice {i.InvoiceNo}"), ct);
        if (sales.Count > 0)
        {
            var (charges, credits) = await receivables.DocumentsAsync(ledger.Id, date, ct);
            await InvoicesAsync(RevaluationLineKind.Receivable, OpenItems.Settle(charges, credits, date), sales, Usage.ReceivableRevaluation);
        }
        var purchases = await db.PurchaseInvoices.AsNoTracking().Where(i => i.LedgerId == ledger.Id && i.Status == DocumentStatus.Posted && !i.IsDebitNote
                && i.Currency != "" && i.Currency != ledger.BaseCurrency && i.ExchangeRate != null && i.Date <= date)
            .ToDictionaryAsync(i => i.Id, i => (i.Currency, i.ExchangeRate!.Value, $"Purchase invoice {i.InvoiceNo}"), ct);
        if (purchases.Count > 0)
        {
            var (charges, credits) = await payables.DocumentsAsync(ledger.Id, date, ct);
            await InvoicesAsync(RevaluationLineKind.Payable, OpenItems.Settle(charges, credits, date), purchases, Usage.PayableRevaluation);
        }
        return (lines, used);
    }

    private static Dictionary<string, string> Format(Dictionary<string, decimal> rates) =>
        rates.ToDictionary(r => r.Key, r => r.Value.ToString("0.##########", CultureInfo.InvariantCulture));

    private static string F(decimal v, int minor) => v.ToString("F" + minor, CultureInfo.InvariantCulture);

    private static FxRevaluationLineDto ToDto(FxRevaluationLine l, int baseMinor) => new(
        Text.Of(l.Kind), l.AccountId, l.PartyId, l.InvoiceId, l.Description, l.Currency,
        l.ForeignAmount.ToString("F" + Currencies.Require(l.Currency).MinorUnits, CultureInfo.InvariantCulture),
        F(l.Before, baseMinor), F(l.After, baseMinor), F(l.Adjustment, baseMinor));

    private static FxRevaluationDto ToDto(FxRevaluation r, Ledger ledger)
    {
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        return new FxRevaluationDto(r.Id, r.RevaluationNo, r.Date, JsonSerializer.Deserialize<Dictionary<string, string>>(r.Rates)!,
            r.Lines.OrderBy(l => l.LineNo).Select(l => ToDto(l, minor)).ToList(), F(r.Lines.Sum(l => l.Adjustment), minor), Text.Of(r.Status),
            r.JournalId, r.ReversalJournalId, r.Version.ToString(CultureInfo.InvariantCulture));
    }
}
