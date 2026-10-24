using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record ReturnBoxDto(string Key, string Section, string Label, string Kind, bool Information);

public sealed record TaxPackDto(string TaxPack, string ReturnName, string? TaxCurrency, IReadOnlyList<WithholdingCategory> WithholdingCategories, IReadOnlyList<ReturnBoxDto> Boxes);

/// <summary>A rate for the whole month instead of the documents' snapshots; the reason is required.</summary>
public sealed record ReturnRateOverride(decimal Rate, string Reason);

public sealed record ReturnBoxAmount(string Key, string Section, string Label, bool Information, string Amount, string? TaxCurrencyAmount, int Lines);

/// <param name="TaxCurrencyBasis">document_snapshots, or override when a month rate was entered.</param>
public sealed record TaxReturnDto(
    string Period, DateOnly From, DateOnly To, string TaxPack, string ReturnName, string Currency, string? TaxCurrency,
    string TaxCurrencyBasis, string? OverrideRate, string? OverrideReason,
    IReadOnlyList<ReturnBoxAmount> Boxes, IReadOnlyList<ReconciliationRow> Reconciliation, IReadOnlyList<TaxLineDto> AdjustedLines, IReadOnlyList<JournalIssue> Issues,
    FiledInfo? Filed = null);

/// <summary>A posting on a tax account in the month that does not come from a purchase or sales document.</summary>
public sealed record OtherPosting(long JournalNo, DateOnly Date, string Account, string? Reference, string? Description, string? Source, string Amount);

/// <param name="LedgerAmount">The month's movement on the accounts with this usage (credit positive for VAT output and withholding, debit for input VAT).</param>
/// <param name="ReturnAmount">What the month's documents put on those accounts.</param>
public sealed record ReconciliationRow(
    string Usage, string Label, IReadOnlyList<string> Accounts, string LedgerAmount, string ReturnAmount, string Difference, bool Matches,
    IReadOnlyList<OtherPosting> OtherPostings);

/// <summary>A box whose current amount differs from the filed one (only after a filed month was reopened and changed).</summary>
public sealed record FiledDifference(string Key, string FiledAmount, string CurrentAmount, string? FiledTaxCurrencyAmount, string? CurrentTaxCurrencyAmount);

public sealed record FiledInfo(DateTimeOffset FiledAt, Guid FiledBy, IReadOnlyList<FiledDifference> Differences);

public sealed record FiledReturnSummary(string Period, DateTimeOffset FiledAt, Guid FiledBy);

public sealed record TaxLineDto(
    string DocumentType, Guid DocumentId, long DocumentNo, DateOnly Date, string Party, string? Reference, int LineNo, string? Description,
    string Amount, string? TaxCurrencyRate, string? TaxCurrencyAmount, string? TaxCalculated, string? TaxAmount, string? TaxReason);

public sealed record ReturnBoxLines(string Period, string Key, string Label, string Amount, string? TaxCurrencyAmount, IReadOnlyList<TaxLineDto> Lines);

/// <summary>
/// The monthly tax return of a ledger's tax pack. The pack defines the boxes; the return is calculated from posted
/// documents dated in the month, so people and AI clients (through MCP) read the same definition and figures.
/// KHR amounts use each document's tax currency snapshot, or one rate for the month when overridden.
/// </summary>
public sealed class TaxReturnService(IBooksDb db, ReadRunner reads, WriteRunner writes)
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

    public Task<TaxReturnDto> ReturnAsync(Principal principal, Guid orgId, Guid ledgerId, string period, ReturnRateOverride? rate, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var calculation = await CalculateAsync(ledgerId, period, rate, ct);
            var current = calculation.ToDto();
            var filed = await db.FiledTaxReturns.AsNoTracking().SingleOrDefaultAsync(f => f.LedgerId == ledgerId && f.Period == calculation.From, ct);
            if (filed is null)
            {
                return current;
            }
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<TaxReturnDto>(filed.Snapshot, BooksJson.Options)!;
            var was = snapshot.Boxes.ToDictionary(b => b.Key);
            var differences = current.Boxes
                .Where(b => !was.TryGetValue(b.Key, out var f) || f.Amount != b.Amount || f.TaxCurrencyAmount != b.TaxCurrencyAmount)
                .Select(b => new FiledDifference(b.Key, was.GetValueOrDefault(b.Key)?.Amount ?? "", b.Amount, was.GetValueOrDefault(b.Key)?.TaxCurrencyAmount, b.TaxCurrencyAmount))
                .ToList();
            return current with { Filed = new FiledInfo(filed.FiledAt, filed.FiledBy, differences) };
        }, ct);

    public Task<IReadOnlyList<FiledReturnSummary>> FiledAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<FiledReturnSummary>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await db.FiledTaxReturns.AsNoTracking().Where(f => f.LedgerId == ledgerId).OrderByDescending(f => f.Period).ToListAsync(ct))
                .Select(f => new FiledReturnSummary(f.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture), f.FiledAt, f.FiledBy)).ToList();
        }, ct);

    /// <summary>
    /// Files a month (administrators, people only): keeps the return as filed and closes the ledger through the
    /// month end (owner decision). A return with missing KHR rates or uncategorized withholding cannot be filed.
    /// </summary>
    public Task<WriteResult> FileAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, string period, ReturnRateOverride? rate, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("tax_return.file", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            Journals.JournalService.RefuseAgentPosting(principal, touchesPosting: true);
            var calculation = await CalculateAsync(ledgerId, period, rate, ct);
            if (await db.FiledTaxReturns.AnyAsync(f => f.LedgerId == ledgerId && f.Period == calculation.From, ct))
            {
                throw BooksProblem.Conflict("RETURN_ALREADY_FILED", $"The return for {calculation.Period} was already filed.");
            }
            var blocking = calculation.Issues.Where(i => i.Code is "TAX_RATE_MISSING" or "WITHHOLDING_UNCATEGORIZED").ToList();
            if (blocking.Count > 0)
            {
                throw new BooksProblem(422, "RETURN_INCOMPLETE", "The return cannot be filed until these issues are fixed.") { Issues = blocking };
            }
            var dto = calculation.ToDto();
            var filed = new FiledTaxReturn(Guid.CreateVersion7(), orgId, ledgerId, calculation.From, BooksJson.Serialize(dto), principal.UserId, scope.Now);
            db.FiledTaxReturns.Add(filed);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var closedBefore = ledger.ClosedThrough;
            if (ledger.ClosedThrough is null || ledger.ClosedThrough < calculation.To)
            {
                ledger.CloseThrough(calculation.To);
            }
            scope.Audit("tax_return.filed", "ledger", ledgerId, new { period = calculation.Period, closed_before = closedBefore, closed_through = ledger.ClosedThrough });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, dto with { Filed = new FiledInfo(filed.FiledAt, filed.FiledBy, []) }, ledger.Version);
        }, ct);

    public Task<ReturnBoxLines> BoxLinesAsync(Principal principal, Guid orgId, Guid ledgerId, string period, string key, ReturnRateOverride? rate, CancellationToken ct) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var calculation = await CalculateAsync(ledgerId, period, rate, ct);
            var box = calculation.Definition.Boxes.SingleOrDefault(b => b.Key == key) ?? throw BooksProblem.NotFound();
            var amount = calculation.Boxes.Single(b => b.Key == key);
            var lines = box.Line is null ? [] : calculation.Lines.Where(l => box.Line(l) is not null).Select(l => calculation.LineDto(l, box.Line(l)!.Value)).ToList();
            return new ReturnBoxLines(calculation.Period, key, box.Label, amount.Amount, amount.TaxCurrencyAmount, lines);
        }, ct);

    internal async Task<ReturnCalculation> CalculateAsync(Guid ledgerId, string period, ReturnRateOverride? rate, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(period + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
        {
            throw BooksProblem.Invalid("period must be a month, yyyy-MM.");
        }
        var to = from.AddMonths(1).AddDays(-1);
        if (rate is not null && (rate.Rate <= 0 || string.IsNullOrWhiteSpace(rate.Reason)))
        {
            throw BooksProblem.Invalid("An override rate must be positive and needs a reason.");
        }
        var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
        var definition = Definition(ledger.TaxPack);
        var slot = TaxPacks.TaxCurrency(ledger.TaxPack);
        var codes = await db.TaxCodes.AsNoTracking().Where(t => t.LedgerId == ledgerId).ToDictionaryAsync(t => t.Id, ct);
        var parties = await db.Parties.AsNoTracking().Where(p => p.LedgerId == ledgerId).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var purchases = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date >= from && i.Date <= to).ToListAsync(ct);
        var sales = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date >= from && i.Date <= to).ToListAsync(ct);
        var ids = purchases.Select(p => p.Id).Concat(sales.Select(s => s.Id)).ToList();
        var snapshots = await db.DocumentTaxCurrencies.AsNoTracking().Where(t => ids.Contains(t.DocumentId)).ToDictionaryAsync(t => t.DocumentId, t => t.Rate, ct);

        TaxKind? Kind(Guid? code) => code is { } id ? codes[id].Kind : null;
        decimal? Rate(Guid documentId) => rate?.Rate ?? (snapshots.TryGetValue(documentId, out var r) ? r : null);
        var lines = purchases.SelectMany(i => i.Lines.Select(l =>
        {
            var sign = i.IsDebitNote ? -1 : 1;
            return new TaxLine(i.SourceKind, i.Id, i.InvoiceNo, i.Date, i.SupplierId, i.Reference, l.LineNo, l.Description, l.AccountId, l.TaxCodeId, Kind(l.TaxCodeId),
                sign * l.NetAmount, sign * l.TaxAmount, sign * l.TaxCalculated, l.TaxReason, l.ValidTaxInvoice,
                l.WithholdingCodeId is { } w ? codes[w].ReturnCategory : null, sign * l.WithholdingAmount, Rate(i.Id));
        })).Concat(sales.SelectMany(i => i.Lines.Select(l =>
        {
            var sign = i.IsCreditNote ? -1 : 1;
            return new TaxLine(i.SourceKind, i.Id, i.InvoiceNo, i.Date, i.CustomerId, i.Reference, l.LineNo, l.Description, l.AccountId, l.TaxCodeId, Kind(l.TaxCodeId),
                sign * l.NetAmount, sign * l.TaxAmount, sign * l.TaxCalculated, l.TaxReason, true, null, 0, Rate(i.Id));
        }))).OrderBy(l => l.Date).ThenBy(l => l.DocumentKind).ThenBy(l => l.DocumentNo).ThenBy(l => l.LineNo).ToList();

        var issues = new List<JournalIssue>();
        var missingRate = lines.Where(l => l.KhrRate is null).Select(l => l.DocumentId).Distinct().Count();
        if (slot is not null && missingRate > 0)
        {
            issues.Add(new JournalIssue("TAX_RATE_MISSING", $"{missingRate} documents have no {slot.Currency} rate snapshot; their {slot.Currency} amounts are left out. Save them again or override the month's rate."));
        }
        var uncategorized = lines.Where(l => l.Withholding != 0 && l.WithholdingCategory is null).Select(l => l.DocumentId).Distinct().Count();
        if (uncategorized > 0)
        {
            issues.Add(new JournalIssue("WITHHOLDING_UNCATEGORIZED", $"{uncategorized} documents use withholding codes without a return category; give those codes a return_category."));
        }

        var drafts = await db.PurchaseInvoices.CountAsync(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Draft && i.Date >= from && i.Date <= to, ct)
            + await db.SalesInvoices.CountAsync(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Draft && i.Date >= from && i.Date <= to, ct);
        if (drafts > 0)
        {
            issues.Add(new JournalIssue("DRAFTS_IN_PERIOD", $"{drafts} draft documents are dated in this month and are not in the return; post or re-date them before filing."));
        }

        var currency = Currencies.Require(ledger.BaseCurrency);
        var taxCurrency = slot is null ? null : Currencies.Require(slot.Currency);
        var usd = new Dictionary<string, decimal>();
        var khr = new Dictionary<string, decimal>();
        var counts = new Dictionary<string, int>();
        foreach (var box in definition.Boxes)
        {
            if (box.Line is { } select)
            {
                var contributing = lines.Select(l => (Line: l, Amount: select(l))).Where(x => x.Amount is not null).ToList();
                usd[box.Key] = contributing.Sum(x => x.Amount!.Value);
                khr[box.Key] = taxCurrency is null ? 0
                    : Math.Round(contributing.Sum(x => x.Amount!.Value * (x.Line.KhrRate ?? 0)), taxCurrency.MinorUnits, MidpointRounding.AwayFromZero);
                counts[box.Key] = contributing.Count;
            }
            else
            {
                usd[box.Key] = box.Formula!(k => usd[k], currency.MinorUnits);
                khr[box.Key] = taxCurrency is null ? 0 : box.Formula!(k => khr[k], taxCurrency.MinorUnits);
                counts[box.Key] = 0;
            }
        }
        var boxes = definition.Boxes.Select(b => new ReturnBoxAmount(b.Key, b.Section, b.Label, b.Information,
            F(usd[b.Key], currency.MinorUnits), taxCurrency is null ? null : F(khr[b.Key], taxCurrency.MinorUnits), counts[b.Key])).ToList();
        var reconciliation = await ReconcileAsync(ledgerId, from, to, lines, currency.MinorUnits, ct);
        return new ReturnCalculation(ledger, definition, period, from, to, currency, taxCurrency, rate, lines, boxes, reconciliation, usd, issues, parties);
    }

    private sealed record Posting(string Usage, string Code, long JournalNo, DateOnly AccountingDate, string? Reference, string? Description, string? SourceKind, decimal Amount);

    private static readonly string[] DocumentKinds = [PurchaseInvoice.Kind, PurchaseInvoice.DebitNoteKind, SalesInvoice.Kind, SalesInvoice.CreditNoteKind];

    /// <summary>
    /// Compares the month's documents with the tax accounts' movement. Manual journals and other postings on those
    /// accounts are listed, because the return does not include them.
    /// </summary>
    private async Task<List<ReconciliationRow>> ReconcileAsync(Guid ledgerId, DateOnly from, DateOnly to, IReadOnlyList<TaxLine> lines, int minor, CancellationToken ct)
    {
        var postings = await db.Database.SqlQuery<Posting>($"""
            SELECT u.usage AS usage, a.code AS code, j.journal_no AS journal_no, r.accounting_date AS accounting_date, r.reference AS reference,
                   r.description AS description, j.source_kind AS source_kind, l.amount AS amount
            FROM journals j
            JOIN journal_revisions r ON r.id = j.effective_revision_id
            JOIN journal_lines l ON l.revision_id = r.id
            JOIN accounts a ON a.id = l.account_id
            JOIN account_usages u ON u.account_id = l.account_id AND u.usage IN ('output_vat', 'input_vat', 'withholding_payable')
            WHERE j.ledger_id = {ledgerId} AND r.accounting_date >= {from} AND r.accounting_date <= {to}
            ORDER BY r.accounting_date, j.journal_no
            """).ToListAsync(ct);
        (string Usage, string Label, int Sign, decimal Expected)[] usages =
        [
            ("output_vat", "Output VAT", -1, lines.Where(l => l.IsSale && l.TaxKind == TaxKind.VatOutput).Sum(l => l.Tax)),
            ("input_vat", "Deductible input VAT", 1, lines.Where(l => l.IsPurchase && l.TaxKind == TaxKind.VatInput && l.ValidTaxInvoice).Sum(l => l.Tax)),
            ("withholding_payable", "Withholding tax payable", -1, lines.Where(l => l.IsPurchase).Sum(l => l.Withholding)),
        ];
        return usages.Select(u =>
        {
            var mine = postings.Where(p => p.Usage == u.Usage).ToList();
            var ledgerAmount = u.Sign * mine.Sum(p => p.Amount);
            var others = mine.Where(p => p.SourceKind is null || !DocumentKinds.Contains(p.SourceKind))
                .Select(p => new OtherPosting(p.JournalNo, p.AccountingDate, p.Code, p.Reference, p.Description, p.SourceKind ?? "journal", F(u.Sign * p.Amount, minor)))
                .ToList();
            return new ReconciliationRow(u.Usage, u.Label, mine.Select(p => p.Code).Distinct().Order().ToList(),
                F(ledgerAmount, minor), F(u.Expected, minor), F(ledgerAmount - u.Expected, minor), ledgerAmount == u.Expected, others);
        }).ToList();
    }

    private static TaxReturnDefinition Definition(string? taxPack) =>
        TaxReturns.For(taxPack) ?? throw BooksProblem.Conflict("NO_TAX_PACK", "This ledger has no tax pack; apply the Cambodia template first.");

    internal static string F(decimal amount, int minor) => amount.ToString("F" + minor, CultureInfo.InvariantCulture);
}

/// <summary>A calculated month: the document lines, the boxes and what the response and filing need.</summary>
internal sealed record ReturnCalculation(
    Ledger Ledger, TaxReturnDefinition Definition, string Period, DateOnly From, DateOnly To, Currency Currency, Currency? TaxCurrency,
    ReturnRateOverride? Override, IReadOnlyList<TaxLine> Lines, IReadOnlyList<ReturnBoxAmount> Boxes, IReadOnlyList<ReconciliationRow> Reconciliation,
    IReadOnlyDictionary<string, decimal> Usd,
    List<JournalIssue> Issues, IReadOnlyDictionary<Guid, string> Parties)
{
    public TaxReturnDto ToDto() => new(
        Period, From, To, Definition.TaxPack, Definition.Name, Currency.Code, TaxCurrency?.Code,
        Override is null ? "document_snapshots" : "override", Override?.Rate.ToString("0.##########", CultureInfo.InvariantCulture), Override?.Reason,
        Boxes, Reconciliation, Lines.Where(l => l.TaxAdjusted).Select(l => LineDto(l, l.Tax)).ToList(), Issues);

    public TaxLineDto LineDto(TaxLine l, decimal amount) => new(
        l.DocumentKind, l.DocumentId, l.DocumentNo, l.Date, Parties.GetValueOrDefault(l.PartyId, "?"), l.Reference, l.LineNo, l.Description,
        TaxReturnService.F(amount, Currency.MinorUnits),
        l.KhrRate?.ToString("0.##########", CultureInfo.InvariantCulture),
        TaxCurrency is null || l.KhrRate is null ? null : TaxReturnService.F(Math.Round(amount * l.KhrRate.Value, TaxCurrency.MinorUnits, MidpointRounding.AwayFromZero), TaxCurrency.MinorUnits),
        l.TaxKind is null ? null : TaxReturnService.F(l.TaxCalculated, Currency.MinorUnits),
        l.TaxKind is null ? null : TaxReturnService.F(l.Tax, Currency.MinorUnits),
        l.TaxReason);
}
