using System.Globalization;
using System.Text.Json;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <summary>A line settles <paramref name="InvoiceId"/>, posts to <paramref name="AccountId"/>, or (neither) goes to the party's balance.</summary>
/// <param name="Amount">Net amount on account lines (VAT excluded); may be negative. Omit on account lines to use quantity × unit price.</param>
/// <param name="SettledAmount">Invoice lines in another currency: the amount of the invoice settled, in the invoice's currency; omit when it is the line's amount converted at the day's rate.</param>
public sealed record PaymentLineInput(
    Guid? InvoiceId, Guid? AccountId, string? Description, Guid? ItemId, decimal? Quantity, decimal? UnitPrice, decimal? Amount,
    Guid? TaxCodeId = null, decimal? TaxOverride = null, string? TaxReason = null, Guid? WithholdingCodeId = null,
    decimal? SettledAmount = null);

/// <param name="ControlAccountId">Party lines: the Payable or Receivable account; omit to use the ledger's only one.</param>
/// <param name="ExchangeRate">For a bank account in another currency: its units per one unit of the ledger's; omit for the rate of the date.</param>
public sealed record PaymentInput(
    Guid BankAccountId, Guid? PartyId, DateOnly Date, string? Reference, string? Description, IReadOnlyList<PaymentLineInput> Lines, Guid? ControlAccountId = null,
    decimal? ExchangeRate = null);

public sealed record PaymentLineDto(
    Guid LineId, string Kind, Guid? InvoiceId, Guid? AccountId, Guid? ControlAccountId, string? Description, Guid? ItemId, string? Quantity, string? UnitPrice,
    string Amount, Guid? TaxCodeId, string TaxCalculated, string TaxAmount, bool TaxAdjusted, string? TaxReason,
    Guid? WithholdingCodeId, string WithholdingAmount, string Total, string? SettledAmount = null, string? ControlAmount = null);

/// <param name="Currency">The bank account's currency; amounts are in it.</param>
public sealed record PaymentDto(
    Guid Id, Guid LedgerId, string Type, long PaymentNo, Guid BankAccountId, Guid? PartyId, DateOnly Date, string? Reference, string? Description,
    IReadOnlyList<PaymentLineDto> Lines, string Total, string Status, IReadOnlyList<JournalIssue> Issues, Guid? JournalId, long? JournalNo, string Version,
    string Currency = "", string? ExchangeRate = null);

/// <summary>
/// Payments and receipts (owner decision, Manager.io style). Save posts when valid; otherwise the document is kept as
/// a draft with the reasons. A posted document whose change cannot be posted stays unchanged (rule 28). AI clients
/// save drafts only.
/// </summary>
public sealed class PaymentService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting)
{
    public Task<IReadOnlyList<PaymentDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, bool receipts, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<PaymentDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await LoadAsync(ledgerId, null, ct)).Where(p => (p.Type == "receipt") == receipts).ToList();
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, PaymentInput input, bool post, bool receipt, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "payment.save" : "payment.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var (status, payment) = await CreateOneAsync(scope, ledger, input, post, receipt, ct);
            return WriteResult.Of(status == "not_posted" ? 422 : 201, (await LoadAsync(ledgerId, payment.Id, ct)).Single(), payment.Version);
        }, ct);

    /// <summary>Saves several payments and receipts as drafts in one transaction, all or none (MCP batches).</summary>
    public Task<WriteResult> CreateDraftsAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId,
        IReadOnlyList<(PaymentInput Input, bool Receipt)> documents, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("payment.save_drafts", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var saved = await Batch.RunAllAsync(documents, d => CreateOneAsync(scope, ledger, d.Input, post: false, d.Receipt, ct));
            var dtos = (await LoadAsync(ledgerId, null, ct)).ToDictionary(d => d.Id);
            return WriteResult.Of(201, saved.Select(s => dtos[s.Payment.Id]).ToList());
        }, ct);

    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid paymentId, PaymentInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "payment.save" : "payment.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var payment = await FindAsync(ledgerId, paymentId, ct);
            scope.CheckVersion(payment.Version);
            var wasPosted = payment.Status == DocumentStatus.Posted;
            if (wasPosted && !post)
            {
                throw BooksProblem.Conflict("DOCUMENT_POSTED", "A posted document cannot go back to draft; save it or delete it.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var before = (await LoadAsync(ledgerId, payment.Id, ct)).Single();
            db.PaymentLines.RemoveRange(payment.Lines);
            var issues = await SetAsync(payment, ledger, input, isNew: false, ct);
            var status = await SaveAsync(scope, ledger, payment, issues, post, ct);
            if (wasPosted && status == "not_posted")
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The change cannot be posted; the document and its posting are unchanged.")
                {
                    Issues = JsonSerializer.Deserialize<List<JournalIssue>>(payment.Issues!, BooksJson.Options),
                };
            }
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, payment.Id, ct)).Single();
            scope.Audit($"{payment.SourceKind}.{status}", payment.SourceKind, payment.Id, new { before, after });
            return WriteResult.Of(status == "not_posted" ? 422 : 200, after, payment.Version);
        }, ct);

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid paymentId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("payment.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var payment = await FindAsync(ledgerId, paymentId, ct);
            scope.CheckVersion(payment.Version);
            if (payment.Status == DocumentStatus.Posted)
            {
                await posting.DeleteAsync(scope, await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct), payment.JournalId!.Value, ct);
            }
            payment.Delete();
            scope.Audit($"{payment.SourceKind}.deleted", payment.SourceKind, payment.Id, new { payment_no = payment.PaymentNo });
            return WriteResult.Of(200, new { id = payment.Id, deleted = true }, payment.Version);
        }, ct);

    internal async Task<(string Status, Payment Payment)> CreateOneAsync(WriteScope scope, Ledger ledger, PaymentInput input, bool post, bool receipt, CancellationToken ct)
    {
        var series = receipt ? Payment.ReceiptKind : Payment.Kind;
        var payment = new Payment(Guid.CreateVersion7(), ledger.OrgId, ledger.Id, await posting.NextNumberAsync(ledger.Id, series, ct), scope.Principal.UserId, scope.Now, receipt);
        var issues = await SetAsync(payment, ledger, input, isNew: true, ct);
        db.Payments.Add(payment);
        var status = await SaveAsync(scope, ledger, payment, issues, post, ct);
        scope.Audit($"{payment.SourceKind}.{status}", payment.SourceKind, payment.Id, new { payment_no = payment.PaymentNo, journal_id = payment.JournalId, issues = payment.Issues });
        await db.SaveChangesAsync(ct);
        return (status, payment);
    }

    /// <summary>Posts a draft as it stands (statement drafts to Suspense); returns posted or not_posted.</summary>
    internal Task<string> PostDraftAsync(WriteScope scope, Ledger ledger, Payment payment, CancellationToken ct) =>
        SaveAsync(scope, ledger, payment, [], post: true, ct);

    private async Task<string> SaveAsync(WriteScope scope, Ledger ledger, Payment payment, List<JournalIssue> documentIssues, bool post, CancellationToken ct)
    {
        if (post)
        {
            JournalService.RefuseAgentPosting(scope.Principal, touchesPosting: true);
        }
        var taxCodes = await TaxCodesAsync(payment, ct);
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var fx = payment.Lines.Any(l => l.Kind != PaymentLineKind.Account && payment.ToBase(l.NetAmount, minor) != l.ControlAmount)
            ? await Inventory.AccountAsync(db, ledger.Id, Usage.FxRealized, ct)
            : (Guid?)null;
        var draft = payment.ToJournal(taxCodes, minor, fx);
        if (!post || documentIssues.Count > 0)
        {
            var issues = documentIssues.Concat(await posting.CheckAsync(ledger, draft, ct)).ToList();
            payment.KeptAsDraft(issues.Count == 0 ? null : BooksJson.Serialize(issues));
            return post ? "not_posted" : "draft";
        }
        var outcome = await posting.PostAsync(scope, ledger, payment.SourceKind, payment.Id, payment.JournalId, draft, ct);
        if (outcome.Journal is { } journal)
        {
            payment.PostedAs(journal.Id);
            return "posted";
        }
        payment.KeptAsDraft(BooksJson.Serialize(outcome.Issues));
        return "not_posted";
    }

    /// <summary>
    /// Checks references (unknown or wrong-kind ones are 400) and builds the lines. Returns document-level issues that
    /// keep the document a draft, such as a party that is not an active supplier or customer.
    /// </summary>
    private async Task<List<JournalIssue>> SetAsync(Payment payment, Ledger ledger, PaymentInput input, bool isNew, CancellationToken ct)
    {
        var issues = new List<JournalIssue>();
        var receipt = payment.IsReceipt;
        var baseMinor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        if (!await db.AccountUsages.AnyAsync(u => u.LedgerId == ledger.Id && u.AccountId == input.BankAccountId && u.Usage == Usage.Bank, ct))
        {
            throw BooksProblem.Invalid("bank_account_id must be an account with the Bank usage.");
        }
        // The payment is in the bank account's currency, at the rate given or the rate of the date (M12.2).
        var currency = await db.BankAccounts.Where(b => b.LedgerId == ledger.Id && b.AccountId == input.BankAccountId).Select(b => b.Currency).SingleOrDefaultAsync(ct)
            ?? ledger.BaseCurrency;
        var rate = await Rates.ForDocumentAsync(db, ledger, currency, input.ExchangeRate, input.Date, ct);
        try
        {
            payment.SetCurrency(currency, rate, ledger.BaseCurrency);
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
        var minor = Currencies.Require(currency).MinorUnits;
        var party = input.PartyId is { } partyId
            ? await db.Parties.SingleOrDefaultAsync(p => p.LedgerId == ledger.Id && p.Id == partyId, ct)
                ?? throw BooksProblem.Invalid("party_id must be a party of this ledger.")
            : null;
        var controls = await db.AccountUsages.Where(u => u.LedgerId == ledger.Id && (u.Usage == Usage.Payable || u.Usage == Usage.Receivable))
            .ToDictionaryAsync(u => u.AccountId, u => u.Usage, ct);
        var usedUsages = new HashSet<Usage>();

        var codes = await db.TaxCodes.AsNoTracking().Where(t => t.LedgerId == ledger.Id).ToDictionaryAsync(t => t.Id, ct);
        Guid? partyControl = null;
        var lines = new List<PaymentLine>();
        foreach (var (line, i) in (input.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => (l, i + 1)))
        {
            // Manager.io style: a line on an account with the Payable or Receivable usage settles with the party; it may
            // name the invoice. Without an account, a line naming an invoice or nothing settles on this document's side.
            if (line.InvoiceId is not null || line.AccountId is null || controls.ContainsKey(line.AccountId.Value))
            {
                var amount = line.Amount ?? throw BooksProblem.Invalid($"Line {i}: amount is required.");
                var usage = line.AccountId is { } chosen ? controls[chosen] : receipt ? Usage.Receivable : Usage.Payable;
                usedUsages.Add(usage);
                if (line.InvoiceId is { } invoiceId)
                {
                    var invoice = usage == Usage.Receivable
                        ? await db.SalesInvoices.Where(x => x.LedgerId == ledger.Id && x.Id == invoiceId && !x.IsCreditNote && x.Status == DocumentStatus.Posted && x.CustomerId == input.PartyId)
                            .Select(x => new { Control = x.ReceivableAccountId, x.Currency, x.ExchangeRate }).SingleOrDefaultAsync(ct)
                        : await db.PurchaseInvoices.Where(x => x.LedgerId == ledger.Id && x.Id == invoiceId && !x.IsDebitNote && x.Status == DocumentStatus.Posted && x.SupplierId == input.PartyId)
                            .Select(x => new { Control = x.PayableAccountId, x.Currency, x.ExchangeRate }).SingleOrDefaultAsync(ct);
                    if (invoice is null || (line.AccountId is { } given && given != invoice.Control))
                    {
                        throw BooksProblem.Invalid($"Line {i}: invoice_id must be a posted {(usage == Usage.Receivable ? "sales" : "purchase")} invoice of the document's party, on that account.");
                    }
                    var invoiceCurrency = invoice.Currency == "" ? ledger.BaseCurrency : invoice.Currency;
                    if (invoiceCurrency == ledger.BaseCurrency && currency == ledger.BaseCurrency)
                    {
                        if (line.SettledAmount is not null)
                        {
                            throw BooksProblem.Invalid($"Line {i}: settled_amount applies only when the invoice or the payment is in another currency.");
                        }
                        lines.Add(PaymentLine.ForInvoice(invoiceId, invoice.Control, line.Description, amount));
                    }
                    else
                    {
                        // Settled in the invoice's currency; it leaves the control account at the invoice's rate, and the
                        // difference from what the bank moved is realized exchange gain or loss (owner decision M12.2).
                        var settled = line.SettledAmount ?? (invoiceCurrency == currency ? amount
                            : Math.Round(payment.ToBase(amount, 10) * (await Rates.ForDocumentAsync(db, ledger, invoiceCurrency, null, input.Date, ct) ?? 1m),
                                Currencies.Require(invoiceCurrency).MinorUnits, MidpointRounding.AwayFromZero));
                        var control = invoice.ExchangeRate is { } invoiceRate ? Math.Round(settled / invoiceRate, baseMinor, MidpointRounding.AwayFromZero) : settled;
                        lines.Add(PaymentLine.ForInvoice(invoiceId, invoice.Control, line.Description, amount, settled, control));
                    }
                }
                else
                {
                    if (line.SettledAmount is not null)
                    {
                        throw BooksProblem.Invalid($"Line {i}: settled_amount applies only to lines naming an invoice.");
                    }
                    // Owner decision (M12.2): where another currency is involved the line names the invoice, so the
                    // exchange difference is realized on it instead of staying in the party's balance.
                    if (currency != ledger.BaseCurrency || await HasForeignInvoicesAsync(ledger, input.PartyId, usage, ct))
                    {
                        throw BooksProblem.Invalid($"Line {i}: name the invoice this settles (invoice_id); a payment in another currency, or a party with invoices "
                            + "in another currency, settles invoice by invoice so the exchange difference is realized.");
                    }
                    var control = line.AccountId ?? (partyControl ??= await ControlAccountAsync(ledger.Id, usage, input.ControlAccountId, ct));
                    lines.Add(PaymentLine.ForParty(control, line.Description, amount, payment.ToBase(amount, baseMinor)));
                }
            }
            else
            {
                var accountId = line.AccountId.Value;
                var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.LedgerId == ledger.Id && a.Id == accountId, ct)
                    ?? throw BooksProblem.Invalid($"Line {i}: account_id must be an account of this ledger.");
                if (account.IsControl)
                {
                    throw BooksProblem.Invalid($"Line {i}: {account.Code} is a control account without the Payable or Receivable usage.");
                }
                if (line.ItemId is { } itemId && !await db.Items.AnyAsync(x => x.LedgerId == ledger.Id && x.Id == itemId, ct))
                {
                    throw BooksProblem.Invalid($"Line {i}: item_id must be an item of this ledger.");
                }
                var net = line.Amount ?? (line is { Quantity: { } q, UnitPrice: { } p }
                    ? decimal.Round(q * p, minor, MidpointRounding.AwayFromZero)
                    : throw BooksProblem.Invalid($"Line {i}: give an amount, or a quantity and a unit price."));
                LineTax? tax = null;
                if (line.TaxCodeId is { } taxId)
                {
                    var code = codes.GetValueOrDefault(taxId) ?? throw BooksProblem.Invalid($"Line {i}: tax_code_id must be a tax code of this ledger.");
                    var allowed = receipt
                        ? code.Kind is TaxKind.VatOutput or TaxKind.VatZeroRated or TaxKind.NonVat
                        : code.Kind is TaxKind.VatInput or TaxKind.VatInputNonDeductible or TaxKind.VatZeroRated or TaxKind.NonVat;
                    if (!allowed)
                    {
                        throw BooksProblem.Invalid($"Line {i}: {(receipt ? "receipt" : "payment")} lines take {(receipt ? "output" : "input")} VAT (creditable or non-creditable), zero-rated or non-VAT codes.");
                    }
                    var canOverride = receipt ? code.Kind == TaxKind.VatOutput : code.Kind is TaxKind.VatInput or TaxKind.VatInputNonDeductible;
                    if (!canOverride && line.TaxOverride is not null and not 0)
                    {
                        throw BooksProblem.Invalid($"Line {i}: zero-rated and non-VAT lines have no tax to override.");
                    }
                    if (!code.AppliesOn(input.Date))
                    {
                        issues.Add(new JournalIssue("TAX_CODE_NOT_EFFECTIVE", $"{code.Name} does not apply on {input.Date:yyyy-MM-dd}.", i));
                    }
                    tax = TaxCalculator.ForLine(net, code.Rate, minor, line.TaxOverride, line.TaxReason);
                }
                var withholding = 0m;
                if (line.WithholdingCodeId is { } whtId)
                {
                    if (receipt)
                    {
                        throw BooksProblem.Invalid($"Line {i}: receipts do not withhold tax; record tax a customer withheld as its own line.");
                    }
                    var code = codes.GetValueOrDefault(whtId) ?? throw BooksProblem.Invalid($"Line {i}: withholding_code_id must be a tax code of this ledger.");
                    if (code.Kind != TaxKind.Withholding)
                    {
                        throw BooksProblem.Invalid($"Line {i}: withholding_code_id must be a withholding code.");
                    }
                    if (!code.AppliesOn(input.Date))
                    {
                        issues.Add(new JournalIssue("TAX_CODE_NOT_EFFECTIVE", $"{code.Name} does not apply on {input.Date:yyyy-MM-dd}.", i));
                    }
                    withholding = TaxCalculator.ForLine(net, code.Rate, minor).Amount;
                }
                lines.Add(PaymentLine.ForAccount(accountId, line.Description, line.ItemId, line.Quantity, line.UnitPrice, net,
                    line.TaxCodeId, tax, line.WithholdingCodeId, withholding));
            }
        }
        if (party is not null && (!party.Active
                || (usedUsages.Contains(Usage.Payable) && !party.IsSupplier) || (usedUsages.Contains(Usage.Receivable) && !party.IsCustomer)
                || (usedUsages.Count == 0 && !(receipt ? party.IsCustomer : party.IsSupplier))))
        {
            issues.Add(usedUsages.Contains(Usage.Payable) || (usedUsages.Count == 0 && !receipt)
                ? new JournalIssue("NOT_AN_ACTIVE_SUPPLIER", "The party is not an active supplier.")
                : new JournalIssue("NOT_AN_ACTIVE_CUSTOMER", "The party is not an active customer."));
        }
        payment.Set(input.BankAccountId, input.PartyId, input.Date, input.Reference, input.Description, lines, isNew);
        return issues;
    }

    /// <summary>Whether the party has posted invoices on this side (sales for receivables, purchases for payables) in another currency.</summary>
    private async Task<bool> HasForeignInvoicesAsync(Ledger ledger, Guid? partyId, Usage usage, CancellationToken ct) =>
        partyId is { } party && (usage == Usage.Receivable
            ? await db.SalesInvoices.AnyAsync(x => x.LedgerId == ledger.Id && x.CustomerId == party && x.Status == DocumentStatus.Posted
                && x.Currency != "" && x.Currency != ledger.BaseCurrency, ct)
            : await db.PurchaseInvoices.AnyAsync(x => x.LedgerId == ledger.Id && x.SupplierId == party && x.Status == DocumentStatus.Posted
                && x.Currency != "" && x.Currency != ledger.BaseCurrency, ct));

    /// <summary>The given account must carry the usage; when omitted, the ledger must have exactly one.</summary>
    private async Task<Guid> ControlAccountAsync(Guid ledgerId, Usage usage, Guid? accountId, CancellationToken ct)
    {
        var accounts = await db.AccountUsages.Where(u => u.LedgerId == ledgerId && u.Usage == usage).Select(u => u.AccountId).ToListAsync(ct);
        var name = Text.Of(usage);
        if (accountId is { } id)
        {
            return accounts.Contains(id) ? id : throw BooksProblem.Invalid($"control_account_id must be an account with the {name} usage.");
        }
        return accounts.Count switch
        {
            1 => accounts[0],
            0 => throw BooksProblem.Invalid($"This ledger has no account with the {name} usage yet; apply the Cambodia template first."),
            _ => throw BooksProblem.Invalid($"This ledger has several {name} accounts; choose one with control_account_id."),
        };
    }

    private async Task<Dictionary<Guid, TaxCode>> TaxCodesAsync(Payment payment, CancellationToken ct)
    {
        var ids = payment.Lines.SelectMany(l => new[] { l.TaxCodeId, l.WithholdingCodeId }).OfType<Guid>().Distinct().ToList();
        return await db.TaxCodes.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
    }

    private async Task<Payment> FindAsync(Guid ledgerId, Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.Include(p => p.Lines).SingleOrDefaultAsync(p => p.LedgerId == ledgerId && p.Id == paymentId, ct)
            ?? throw BooksProblem.NotFound();
        return payment.Status == DocumentStatus.Deleted ? throw BooksProblem.Conflict("DOCUMENT_DELETED", "This document was deleted.") : payment;
    }

    private async Task<IReadOnlyList<PaymentDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct)
    {
        var baseCurrency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
        var baseMinor = Currencies.Require(baseCurrency).MinorUnits;
        static string? Q(decimal? v) => v?.ToString("0.####", CultureInfo.InvariantCulture);
        var rows = await db.Payments.AsNoTracking().Include(p => p.Lines)
            .Where(p => p.LedgerId == ledgerId && (id == null || p.Id == id))
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.PaymentNo)
            .Select(p => new { p, JournalNo = db.Journals.Where(j => j.Id == p.JournalId).Select(j => (long?)j.JournalNo).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(x =>
        {
            var currency = x.p.Currency == "" ? baseCurrency : x.p.Currency;
            var minor = Currencies.Require(currency).MinorUnits;
            string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
            string B(decimal v) => v.ToString("F" + baseMinor, CultureInfo.InvariantCulture);
            return new PaymentDto(
                x.p.Id, x.p.LedgerId, x.p.IsReceipt ? "receipt" : "payment", x.p.PaymentNo, x.p.BankAccountId, x.p.PartyId, x.p.Date, x.p.Reference, x.p.Description,
                x.p.Lines.OrderBy(l => l.LineNo).Select(l => new PaymentLineDto(l.LineId, Text.Of(l.Kind), l.InvoiceId, l.AccountId, l.ControlAccountId, l.Description,
                    l.ItemId, Q(l.Quantity), Q(l.UnitPrice), F(l.NetAmount), l.TaxCodeId, F(l.TaxCalculated), F(l.TaxAmount), l.TaxAdjusted, l.TaxReason,
                    l.WithholdingCodeId, F(l.WithholdingAmount), F(l.Total), Q(l.SettledAmount),
                    l.Kind == PaymentLineKind.Account ? null : B(l.ControlAmount))).ToList(),
                F(x.p.Total), Text.Of(x.p.Status), x.p.Issues is null ? [] : JsonSerializer.Deserialize<List<JournalIssue>>(x.p.Issues, BooksJson.Options)!,
                x.p.JournalId, x.JournalNo, x.p.Version.ToString(CultureInfo.InvariantCulture), currency,
                x.p.ExchangeRate?.ToString("0.##########", CultureInfo.InvariantCulture));
        }).ToList();
    }
}
