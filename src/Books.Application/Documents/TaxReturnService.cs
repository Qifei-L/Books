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

public sealed record TaxPackDto(string TaxPack, int DefinitionVersion, string ReturnName, string? TaxCurrency, IReadOnlyList<WithholdingCategory> WithholdingCategories, IReadOnlyList<ReturnBoxDto> Boxes);

/// <summary>A rate for the whole month instead of the documents' snapshots; the reason is required.</summary>
public sealed record ReturnRateOverride(decimal Rate, string Reason);

public sealed record ReturnBoxAmount(string Key, string Section, string Label, bool Information, string Amount, string? TaxCurrencyAmount, int Lines);

/// <param name="TaxCurrencyBasis">document_snapshots, or override when a month rate was entered.</param>
public sealed record TaxReturnDto(
    string Period, DateOnly From, DateOnly To, string TaxPack, int DefinitionVersion, string ReturnName, string Currency, string? TaxCurrency,
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
public sealed class TaxReturnService(IBooksDb db, ReadRunner reads, WriteRunner writes, ReturnDefinitionService definitions)
{
    public Task<TaxPackDto> PackAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.Id == ledgerId, ct);
            var definition = await definitions.EffectiveAsync(ledger, DateOnly.MaxValue, null, ct);
            return new TaxPackDto(definition.TaxPack, definition.Version, definition.Name, TaxPacks.TaxCurrency(ledger.TaxPack)?.Currency, definition.WithholdingCategories,
                definition.Boxes.Select(b => new ReturnBoxDto(b.Key, b.Section, b.Label, b.Formula is null ? "lines" : "formula", b.Information)).ToList());
        }, ct);

    public Task<TaxReturnDto> ReturnAsync(Principal principal, Guid orgId, Guid ledgerId, string period, ReturnRateOverride? rate, CancellationToken ct,
        int? definitionVersion = null) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var calculation = await CalculateAsync(ledgerId, period, rate, ct, definitionVersion);
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
    /// month end (owner decision). A return with missing KHR rates, uncategorized withholding or drafts dated in the
    /// month cannot be filed.
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
            // Drafts dated in the month would be stuck behind the close, so they block filing too (owner decision).
            var blocking = calculation.Issues.Where(i => i.Code is "TAX_RATE_MISSING" or "WITHHOLDING_UNCATEGORIZED" or "DRAFTS_IN_PERIOD").ToList();
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

    public Task<ReturnBoxLines> BoxLinesAsync(Principal principal, Guid orgId, Guid ledgerId, string period, string key, ReturnRateOverride? rate, CancellationToken ct,
        int? definitionVersion = null) =>
        reads.SnapshotAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var calculation = await CalculateAsync(ledgerId, period, rate, ct, definitionVersion);
            var box = calculation.Definition.Boxes.SingleOrDefault(b => b.Key == key) ?? throw BooksProblem.NotFound();
            var amount = calculation.Boxes.Single(b => b.Key == key);
            var lines = box.Line is null ? [] : calculation.Lines.Where(l => box.Line(l) is not null).Select(l => calculation.LineDto(l, box.Line(l)!.Value)).ToList();
            return new ReturnBoxLines(calculation.Period, key, box.Label, amount.Amount, amount.TaxCurrencyAmount, lines);
        }, ct);

    internal async Task<ReturnCalculation> CalculateAsync(Guid ledgerId, string period, ReturnRateOverride? rate, CancellationToken ct, int? definitionVersion = null)
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
        var definition = await definitions.EffectiveAsync(ledger, from, definitionVersion, ct);
        var slot = TaxPacks.TaxCurrency(ledger.TaxPack);
        var codes = await db.TaxCodes.AsNoTracking().Where(t => t.LedgerId == ledgerId).ToDictionaryAsync(t => t.Id, ct);
        var partyRows = await db.Parties.AsNoTracking().Where(p => p.LedgerId == ledgerId).Select(p => new { p.Id, p.Name, p.TaxId }).ToListAsync(ct);
        var parties = partyRows.ToDictionary(p => p.Id, p => p.Name);
        // A taxable person, for the return's purposes, is a customer or supplier with a tax ID.
        var taxable = partyRows.Where(p => !string.IsNullOrWhiteSpace(p.TaxId)).Select(p => p.Id).ToHashSet();

        var purchases = await db.PurchaseInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date >= from && i.Date <= to).ToListAsync(ct);
        var sales = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Posted && i.Date >= from && i.Date <= to).ToListAsync(ct);
        var ids = purchases.Select(p => p.Id).Concat(sales.Select(s => s.Id)).ToList();
        var snapshots = await db.DocumentTaxCurrencies.AsNoTracking().Where(t => ids.Contains(t.DocumentId)).ToDictionaryAsync(t => t.DocumentId, t => t.Rate, ct);

        var baseMinor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        TaxKind? Kind(Guid? code) => code is { } id ? codes[id].Kind : null;
        string? Category(Guid? code) => code is { } id && codes[id].Kind is TaxKind.VatInput or TaxKind.VatOutput ? codes[id].ReturnCategory : null;
        decimal? Rate(Guid documentId) => rate?.Rate ?? (snapshots.TryGetValue(documentId, out var r) ? r : null);
        IEnumerable<TaxLine> lines = purchases.SelectMany(i => i.Lines.Select(l =>
        {
            var sign = i.IsDebitNote ? -1 : 1;
            decimal B(decimal amount) => sign * i.ToBase(amount, baseMinor);
            return new TaxLine(i.SourceKind, i.Id, i.InvoiceNo, i.Date, i.SupplierId, i.Reference, l.LineNo, l.Description, l.AccountId, l.TaxCodeId, Kind(l.TaxCodeId),
                B(l.NetAmount), B(l.TaxAmount), B(l.TaxCalculated), l.TaxReason,
                l.WithholdingCodeId is { } w ? codes[w].ReturnCategory : null, B(l.WithholdingAmount), Rate(i.Id), PartyTaxable: taxable.Contains(i.SupplierId),
                TaxCategory: Category(l.TaxCodeId));
        })).Concat(sales.SelectMany(i => i.Lines.Select(l =>
        {
            var sign = i.IsCreditNote ? -1 : 1;
            decimal B(decimal amount) => sign * i.ToBase(amount, baseMinor);
            return new TaxLine(i.SourceKind, i.Id, i.InvoiceNo, i.Date, i.CustomerId, i.Reference, l.LineNo, l.Description, l.AccountId, l.TaxCodeId, Kind(l.TaxCodeId),
                B(l.NetAmount), B(l.TaxAmount), B(l.TaxCalculated), l.TaxReason, null, 0, Rate(i.Id), PartyTaxable: taxable.Contains(i.CustomerId),
                TaxCategory: Category(l.TaxCodeId), OtherTax: B(l.OtherTaxAmount), OtherTaxCategory: l.OtherTaxCodeId is { } o ? codes[o].ReturnCategory : null,
                StateCharge: B(l.StateChargeAmount), StateChargeCategory: l.StateChargeCodeId is { } sc ? codes[sc].ReturnCategory : null);
        })));

        // Journal postings on tax accounts that do not come from purchase or sales documents (manual corrections):
        // they count in the boxes that take postings, at the month-end rate (or the month's override).
        var postings = await db.Database.SqlQuery<Posting>($"""
            SELECT u.usage AS usage, a.code AS code, j.id AS journal_id, j.journal_no AS journal_no, l.line_no AS line_no, l.account_id AS account_id,
                   r.accounting_date AS accounting_date, r.reference AS reference, COALESCE(l.description, r.description) AS description,
                   j.source_kind AS source_kind, l.tax_code_id AS tax_code_id, l.tax_direction AS tax_direction, l.amount AS amount
            FROM journals j
            JOIN journal_revisions r ON r.id = j.effective_revision_id
            JOIN journal_lines l ON l.revision_id = r.id
            JOIN accounts a ON a.id = l.account_id
            JOIN account_usages u ON u.account_id = l.account_id AND u.usage IN ('output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable')
            WHERE j.ledger_id = {ledgerId} AND r.accounting_date >= {from} AND r.accounting_date <= {to}
            ORDER BY r.accounting_date, j.journal_no, l.line_no
            """).ToListAsync(ct);
        var monthEnd = slot is null ? null : await db.ExchangeRates.AsNoTracking()
            .Where(r => r.OrgId == ledger.OrgId && r.FromCurrency == ledger.BaseCurrency && r.ToCurrency == slot.Currency && r.Date <= to)
            .OrderByDescending(r => r.Date).Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct);
        var journalLines = postings.Where(p => p.SourceKind is null || !DocumentKinds.Contains(p.SourceKind)).Select(p =>
        {
            var amount = Sign(p.Usage) * p.Amount;
            var kind = p.TaxCodeId is { } codeId && codes.TryGetValue(codeId, out var code) ? code.Kind : (TaxKind?)null;
            var direction = Direction(p.TaxDirection);
            // A manual VAT line with a direction belongs in the return's line boxes; do not also count it in the
            // posting boxes, or the same tax would be reported twice.
            var postingUsage = kind is TaxKind.VatOutput or TaxKind.VatInput or TaxKind.VatInputNonDeductible && direction is not null
                ? null
                : p.Usage;
            return new TaxLine(TaxLine.JournalKind, p.JournalId, p.JournalNo, p.AccountingDate, Guid.Empty, p.Reference, p.LineNo, p.Description, p.AccountId,
                p.TaxCodeId, kind, 0, amount, amount, null, null, 0, rate?.Rate ?? monthEnd, postingUsage,
                TaxCategory: Category(p.TaxCodeId), TaxDirection: direction);
        });
        // Payments and receipts: lines posted straight to an account count when they carry a tax code or withholding
        // (a fee, a transfer or tax a customer withheld is not a sale or purchase), at the month-end rate.
        var payments = await db.Payments.AsNoTracking().Include(p => p.Lines)
            .Where(p => p.LedgerId == ledgerId && p.Status == DocumentStatus.Posted && p.Date >= from && p.Date <= to).ToListAsync(ct);
        var paymentLines = payments.SelectMany(p => p.Lines
            .Where(l => l.Kind == PaymentLineKind.Account && (l.TaxCodeId is not null || l.WithholdingCodeId is not null))
            .Select(l => new TaxLine(p.SourceKind, p.Id, p.PaymentNo, p.Date, p.PartyId ?? Guid.Empty, p.Reference, l.LineNo, l.Description, l.AccountId!.Value,
                l.TaxCodeId, Kind(l.TaxCodeId), p.ToBase(l.NetAmount, baseMinor), p.ToBase(l.TaxAmount, baseMinor), p.ToBase(l.TaxCalculated, baseMinor), l.TaxReason,
                l.WithholdingCodeId is { } w ? codes[w].ReturnCategory : null, p.ToBase(l.WithholdingAmount, baseMinor),
                rate?.Rate ?? (p.ExchangeRate is { } own && p.Currency == slot?.Currency ? own : monthEnd),
                PartyTaxable: p.PartyId is { } party && taxable.Contains(party), TaxCategory: Category(l.TaxCodeId))));
        // Payroll results: the month's posted pay runs; salary tax uses the rate of the 15th (04-cambodia-tax).
        var payRuns = await db.PayRuns.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.LedgerId == ledgerId && r.Status == DocumentStatus.Posted && r.Period == from).ToListAsync(ct);
        var fifteenth = slot is null || payRuns.Count == 0 ? null : await db.ExchangeRates.AsNoTracking()
            .Where(r => r.OrgId == ledger.OrgId && r.FromCurrency == ledger.BaseCurrency && r.ToCurrency == slot.Currency && r.Date <= from.AddDays(14))
            .OrderByDescending(r => r.Date).Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct);
        // Lines in the tax currency (KHR from the payroll system) keep their own amounts: scaled to the ledger's
        // currency and back at the pay run's rate, so the KHR boxes equal what the payroll system reported.
        var payrollLines = payRuns.SelectMany(r => r.Lines.Select(l => new TaxLine(PayRun.Kind, r.Id, r.PayRunNo, r.Date, Guid.Empty, r.Reference, l.LineNo,
            $"{l.EmployeeCode} {l.Name}", Guid.Empty, null, null, 0, 0, 0, null, null, 0,
            r.Rate is { } own && r.Currency == slot?.Currency ? own : rate?.Rate ?? fifteenth, Payroll: l, PayrollScale: r.Rate is { } payRate ? 1 / payRate : 1)));
        lines = lines.Concat(journalLines).Concat(paymentLines).Concat(payrollLines).OrderBy(l => l.Date).ThenBy(l => l.DocumentKind).ThenBy(l => l.DocumentNo).ThenBy(l => l.LineNo).ToList();

        var issues = new List<JournalIssue>();
        var taxLines = (List<TaxLine>)lines;
        var missingRate = taxLines.Where(l => l.KhrRate is null).Select(l => l.DocumentId).Distinct().Count();
        if (slot is not null && missingRate > 0)
        {
            issues.Add(new JournalIssue("TAX_RATE_MISSING", $"{missingRate} documents or journals have no {slot.Currency} rate (snapshot or month-end rate); their {slot.Currency} amounts are left out. Save them again, add the rate or override the month's rate."));
        }
        var uncategorized = taxLines.Where(l => l.Withholding != 0 && l.WithholdingCategory is null).Select(l => l.DocumentId).Distinct().Count();
        if (uncategorized > 0)
        {
            issues.Add(new JournalIssue("WITHHOLDING_UNCATEGORIZED", $"{uncategorized} documents use withholding codes without a return category; give those codes a return_category."));
        }

        var drafts = await db.PurchaseInvoices.CountAsync(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Draft && i.Date >= from && i.Date <= to, ct)
            + await db.SalesInvoices.CountAsync(i => i.LedgerId == ledgerId && i.Status == DocumentStatus.Draft && i.Date >= from && i.Date <= to, ct);
        if (drafts > 0)
        {
            issues.Add(new JournalIssue("DRAFTS_IN_PERIOD", $"{drafts} draft documents are dated in this month and are not in the return; post, delete or re-date them before filing."));
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
                var contributing = taxLines.Select(l => (Line: l, Amount: select(l))).Where(x => x.Amount is not null).ToList();
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
        var reconciliation = Reconcile(postings, taxLines, currency.MinorUnits);
        return new ReturnCalculation(ledger, definition, period, from, to, currency, taxCurrency, rate, taxLines, boxes, reconciliation, usd, issues, parties);
    }

    private sealed record Posting(
        string Usage, string Code, Guid JournalId, long JournalNo, int LineNo, Guid AccountId, DateOnly AccountingDate, string? Reference, string? Description,
        string? SourceKind, Guid? TaxCodeId, string? TaxDirection, decimal Amount);

    /// <summary>Liability usages count credits as positive; input VAT counts debits.</summary>
    private static int Sign(string usage) => usage is "input_vat" or "non_deductible_vat" ? 1 : -1;

    private static TaxDirection? Direction(string? value) => value switch
    {
        "sale" => TaxDirection.Sale,
        "sale_adjustment" => TaxDirection.SaleAdjustment,
        "purchase" => TaxDirection.Purchase,
        "purchase_adjustment" => TaxDirection.PurchaseAdjustment,
        _ => null,
    };

    private static readonly string[] DocumentKinds =
        [PurchaseInvoice.Kind, PurchaseInvoice.DebitNoteKind, SalesInvoice.Kind, SalesInvoice.CreditNoteKind, Payment.Kind, Payment.ReceiptKind];

    /// <summary>
    /// Compares the return with the tax accounts' movement. Journal postings count in the return (owner decision) and
    /// are listed, so a difference means a document line the return does not pick up.
    /// </summary>
    private static List<ReconciliationRow> Reconcile(List<Posting> postings, IReadOnlyList<TaxLine> lines, int minor)
    {
        decimal Journals(string usage) => lines.Where(l => l.PostingUsage == usage).Sum(l => l.Tax);
        (string Usage, string Label, decimal Expected)[] usages =
        [
            ("output_vat", "Output VAT", lines.Where(l => l.IsSale && l.TaxKind == TaxKind.VatOutput).Sum(l => l.Tax) + Journals("output_vat")),
            ("input_vat", "Deductible input VAT", lines.Where(l => l.IsPurchase && l.TaxKind == TaxKind.VatInput).Sum(l => l.Tax) + Journals("input_vat")),
            ("non_deductible_vat", "Non-creditable input VAT", lines.Where(l => l.IsPurchase && l.TaxKind == TaxKind.VatInputNonDeductible).Sum(l => l.Tax) + Journals("non_deductible_vat")),
            ("withholding_payable", "Withholding tax payable", lines.Where(l => l.IsPurchase).Sum(l => l.Withholding) + Journals("withholding_payable")),
        ];
        return usages.Select(u =>
        {
            var mine = postings.Where(p => p.Usage == u.Usage).ToList();
            var ledgerAmount = Sign(u.Usage) * mine.Sum(p => p.Amount);
            var others = mine.Where(p => p.SourceKind is null || !DocumentKinds.Contains(p.SourceKind))
                .Select(p => new OtherPosting(p.JournalNo, p.AccountingDate, p.Code, p.Reference, p.Description, p.SourceKind ?? "journal", F(Sign(u.Usage) * p.Amount, minor)))
                .ToList();
            return new ReconciliationRow(u.Usage, u.Label, mine.Select(p => p.Code).Distinct().Order().ToList(),
                F(ledgerAmount, minor), F(u.Expected, minor), F(ledgerAmount - u.Expected, minor), ledgerAmount == u.Expected, others);
        }).ToList();
    }

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
        Period, From, To, Definition.TaxPack, Definition.Version, Definition.Name, Currency.Code, TaxCurrency?.Code,
        Override is null ? "document_snapshots" : "override", Override?.Rate.ToString("0.##########", CultureInfo.InvariantCulture), Override?.Reason,
        Boxes, Reconciliation, Lines.Where(l => l.TaxAdjusted).Select(l => LineDto(l, l.Tax)).ToList(), Issues);

    public TaxLineDto LineDto(TaxLine l, decimal amount) => new(
        l.DocumentKind, l.DocumentId, l.DocumentNo, l.Date, l.PartyId == Guid.Empty ? "" : Parties.GetValueOrDefault(l.PartyId, "?"), l.Reference, l.LineNo, l.Description,
        TaxReturnService.F(amount, Currency.MinorUnits),
        l.KhrRate?.ToString("0.##########", CultureInfo.InvariantCulture),
        TaxCurrency is null || l.KhrRate is null ? null : TaxReturnService.F(Math.Round(amount * l.KhrRate.Value, TaxCurrency.MinorUnits, MidpointRounding.AwayFromZero), TaxCurrency.MinorUnits),
        l.TaxKind is null ? null : TaxReturnService.F(l.TaxCalculated, Currency.MinorUnits),
        l.TaxKind is null ? null : TaxReturnService.F(l.Tax, Currency.MinorUnits),
        l.TaxReason);
}
