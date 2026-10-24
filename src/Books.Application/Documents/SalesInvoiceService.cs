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

/// <param name="Amount">Net amount; when omitted it is quantity × unit price, rounded to the currency.</param>
/// <param name="TaxOverride">A VAT amount entered by a person instead of the calculated one.</param>
/// <param name="OtherTaxCodeId">A specific, accommodation or public lighting tax charged on the line, on top of VAT.</param>
public sealed record SalesLineInput(
    Guid AccountId, string? Description, Guid? ItemId, decimal? Quantity, decimal? UnitPrice, decimal? Amount,
    Guid? TaxCodeId, decimal? TaxOverride, string? TaxReason, Guid? OtherTaxCodeId = null);

/// <param name="ReceivableAccountId">Omit to use the ledger's only Receivable account.</param>
/// <param name="DueDate">Omit for invoice date + the customer's payment terms.</param>
/// <param name="OriginalInvoiceId">Credit notes only: the invoice being corrected.</param>
public sealed record SalesInvoiceInput(
    Guid CustomerId, Guid? ReceivableAccountId, DateOnly Date, DateOnly? DueDate, string? Reference, string? Description, IReadOnlyList<SalesLineInput> Lines,
    Guid? OriginalInvoiceId = null, TaxCurrencyInput? TaxCurrency = null);

public sealed record SalesLineDto(
    Guid LineId, Guid AccountId, string? Description, Guid? ItemId, string? Quantity, string? UnitPrice, string NetAmount,
    Guid? TaxCodeId, string TaxCalculated, string TaxAmount, bool TaxAdjusted, string? TaxReason, Guid? OtherTaxCodeId, string OtherTaxAmount);

public sealed record SalesInvoiceDto(
    Guid Id, Guid LedgerId, string Type, long InvoiceNo, Guid? OriginalInvoiceId, Guid CustomerId, Guid ReceivableAccountId, DateOnly Date, DateOnly DueDate,
    string? Reference, string? Description, IReadOnlyList<SalesLineDto> Lines,
    string NetTotal, string OtherTaxTotal, string TaxTotal, string GrossTotal, TaxCurrencyDto? TaxCurrency,
    string Status, IReadOnlyList<JournalIssue> Issues, Guid? JournalId, long? JournalNo, string Version);

/// <summary>
/// Sales invoices and credit notes. Save posts when valid; otherwise the document is kept as a draft with the
/// reasons. A posted document whose change cannot be posted stays unchanged (rule 28). AI clients save drafts only.
/// </summary>
public sealed class SalesInvoiceService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting, TaxCurrencyService taxCurrency)
{
    public Task<IReadOnlyList<SalesInvoiceDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct, bool creditNotes = false) =>
        reads.RunAsync<IReadOnlyList<SalesInvoiceDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return (await LoadAsync(ledgerId, null, ct)).Where(i => (i.Type == "credit_note") == creditNotes).ToList();
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, SalesInvoiceInput input, bool post, CancellationToken ct, bool creditNote = false) =>
        writes.RunAsync(principal, new WriteRequest(post ? "sales_invoice.save" : "sales_invoice.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var (status, invoice) = await CreateOneAsync(scope, ledger, input, post, creditNote, ct);
            return WriteResult.Of(status == "not_posted" ? 422 : 201, (await LoadAsync(ledgerId, invoice.Id, ct)).Single(), invoice.Version);
        }, ct);

    /// <summary>Saves several invoices and credit notes as drafts in one transaction, all or none (MCP batches).</summary>
    public Task<WriteResult> CreateDraftsAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId,
        IReadOnlyList<(SalesInvoiceInput Input, bool CreditNote)> documents, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("sales_invoice.save_drafts", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var saved = await Batch.RunAllAsync(documents, d => CreateOneAsync(scope, ledger, d.Input, post: false, d.CreditNote, ct));
            var dtos = (await LoadAsync(ledgerId, null, ct)).ToDictionary(d => d.Id);
            return WriteResult.Of(201, saved.Select(s => dtos[s.Invoice.Id]).ToList());
        }, ct);

    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid invoiceId, SalesInvoiceInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "sales_invoice.save" : "sales_invoice.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var invoice = await FindAsync(ledgerId, invoiceId, ct);
            scope.CheckVersion(invoice.Version);
            var wasPosted = invoice.Status == DocumentStatus.Posted;
            if (wasPosted && !post)
            {
                throw BooksProblem.Conflict("DOCUMENT_POSTED", "A posted document cannot go back to draft; save it or delete it.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var before = (await LoadAsync(ledgerId, invoice.Id, ct)).Single();
            db.SalesInvoiceLines.RemoveRange(invoice.Lines);
            var issues = await SetAsync(invoice, ledger, input, isNew: false, ct);
            var status = await SaveAsync(scope, ledger, invoice, issues, post, ct);
            if (wasPosted && status == "not_posted")
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The change cannot be posted; the invoice and its posting are unchanged.")
                {
                    Issues = JsonSerializer.Deserialize<List<JournalIssue>>(invoice.Issues!, BooksJson.Options),
                };
            }
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, invoice.Id, ct)).Single();
            scope.Audit($"sales_invoice.{status}", "sales_invoice", invoice.Id, new { before, after });
            return WriteResult.Of(status == "not_posted" ? 422 : 200, after, invoice.Version);
        }, ct);

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid invoiceId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("sales_invoice.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var invoice = await FindAsync(ledgerId, invoiceId, ct);
            scope.CheckVersion(invoice.Version);
            if (await db.PaymentLines.AnyAsync(l => l.InvoiceId == invoice.Id
                    && db.Payments.Any(p => p.Id == l.PaymentId && p.Status != DocumentStatus.Deleted), ct))
            {
                throw BooksProblem.Conflict("INVOICE_SETTLED", "A payment or receipt settles this invoice; change or delete that line first.");
            }
            if (invoice.Status == DocumentStatus.Posted)
            {
                await posting.DeleteAsync(scope, await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct), invoice.JournalId!.Value, ct);
            }
            invoice.Delete();
            scope.Audit("sales_invoice.deleted", "sales_invoice", invoice.Id, new { invoice_no = invoice.InvoiceNo });
            return WriteResult.Of(200, new { id = invoice.Id, deleted = true }, invoice.Version);
        }, ct);

    private async Task<(string Status, SalesInvoice Invoice)> CreateOneAsync(WriteScope scope, Ledger ledger, SalesInvoiceInput input, bool post, bool creditNote, CancellationToken ct)
    {
        var series = creditNote ? SalesInvoice.CreditNoteKind : SalesInvoice.Kind;
        var invoice = new SalesInvoice(Guid.CreateVersion7(), ledger.OrgId, ledger.Id, await posting.NextNumberAsync(ledger.Id, series, ct), scope.Principal.UserId, scope.Now, creditNote);
        var issues = await SetAsync(invoice, ledger, input, isNew: true, ct);
        db.SalesInvoices.Add(invoice);
        var status = await SaveAsync(scope, ledger, invoice, issues, post, ct);
        scope.Audit($"sales_invoice.{status}", "sales_invoice", invoice.Id, new { invoice_no = invoice.InvoiceNo, journal_id = invoice.JournalId, issues = invoice.Issues });
        await db.SaveChangesAsync(ct);
        return (status, invoice);
    }

    /// <summary>Posts or keeps as draft: posted, draft (on purpose) or not_posted (kept as draft with reasons).</summary>
    private async Task<string> SaveAsync(WriteScope scope, Ledger ledger, SalesInvoice invoice, List<JournalIssue> documentIssues, bool post, CancellationToken ct)
    {
        if (post)
        {
            JournalService.RefuseAgentPosting(scope.Principal, touchesPosting: true);
        }
        var taxCodes = await TaxCodesAsync(invoice, ct);
        if (!post || documentIssues.Count > 0)
        {
            var issues = documentIssues.Concat(await posting.CheckAsync(ledger, invoice.ToJournal(taxCodes), ct)).ToList();
            invoice.KeptAsDraft(issues.Count == 0 ? null : BooksJson.Serialize(issues));
            return post ? "not_posted" : "draft";
        }
        var outcome = await posting.PostAsync(scope, ledger, invoice.SourceKind, invoice.Id, invoice.JournalId, invoice.ToJournal(taxCodes), ct);
        if (outcome.Journal is { } journal)
        {
            invoice.PostedAs(journal.Id);
            return "posted";
        }
        invoice.KeptAsDraft(BooksJson.Serialize(outcome.Issues));
        return "not_posted";
    }

    /// <summary>
    /// Checks references (unknown or wrong-kind ones are 400) and builds the lines with VAT per line. Returns
    /// document-level issues that keep the invoice a draft, such as a party that is not an active customer.
    /// </summary>
    private async Task<List<JournalIssue>> SetAsync(SalesInvoice invoice, Ledger ledger, SalesInvoiceInput input, bool isNew, CancellationToken ct)
    {
        var issues = new List<JournalIssue>();
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var customer = await db.Parties.SingleOrDefaultAsync(p => p.LedgerId == ledger.Id && p.Id == input.CustomerId, ct)
            ?? throw BooksProblem.Invalid("customer_id must be a party of this ledger.");
        if (!customer.IsCustomer || !customer.Active)
        {
            issues.Add(new JournalIssue("NOT_AN_ACTIVE_CUSTOMER", "The party is not an active customer."));
        }
        var receivable = await ReceivableAccountAsync(ledger.Id, input.ReceivableAccountId, ct);

        var lines = new List<SalesInvoiceLine>();
        var codes = await db.TaxCodes.AsNoTracking().Where(t => t.LedgerId == ledger.Id).ToDictionaryAsync(t => t.Id, ct);
        foreach (var (line, i) in (input.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select((l, i) => (l, i + 1)))
        {
            if (!await db.Accounts.AnyAsync(a => a.LedgerId == ledger.Id && a.Id == line.AccountId, ct))
            {
                throw BooksProblem.Invalid($"Line {i}: account_id must be an account of this ledger.");
            }
            var tracked = false;
            if (line.ItemId is { } itemId)
            {
                var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(x => x.LedgerId == ledger.Id && x.Id == itemId, ct)
                    ?? throw BooksProblem.Invalid($"Line {i}: item_id must be an item of this ledger.");
                tracked = item.TrackInventory;
                if (tracked && line.Quantity is null or 0)
                {
                    throw BooksProblem.Invalid($"Line {i}: {item.Name} is an inventory item; give its quantity.");
                }
            }
            var net = line.Amount ?? (line is { Quantity: { } q, UnitPrice: { } p }
                ? decimal.Round(q * p, minor, MidpointRounding.AwayFromZero)
                : throw BooksProblem.Invalid($"Line {i}: give an amount, or a quantity and a unit price."));
            // Specific, accommodation and public lighting tax: on the net amount, and part of the VAT base.
            var otherTax = 0m;
            if (line.OtherTaxCodeId is { } otherId)
            {
                var other = codes.GetValueOrDefault(otherId) ?? throw BooksProblem.Invalid($"Line {i}: other_tax_code_id must be a tax code of this ledger.");
                if (other.Kind != TaxKind.OtherTax)
                {
                    throw BooksProblem.Invalid($"Line {i}: other_tax_code_id must be an other tax code (specific, accommodation or public lighting).");
                }
                if (!other.AppliesOn(input.Date))
                {
                    issues.Add(new JournalIssue("TAX_CODE_NOT_EFFECTIVE", $"{other.Name} does not apply on {input.Date:yyyy-MM-dd}.", i));
                }
                otherTax = TaxCalculator.ForLine(net, other.Rate, minor).Amount;
            }
            LineTax? tax = null;
            if (line.TaxCodeId is { } taxId)
            {
                var code = codes.GetValueOrDefault(taxId) ?? throw BooksProblem.Invalid($"Line {i}: tax_code_id must be a tax code of this ledger.");
                if (code.Kind is not (TaxKind.VatOutput or TaxKind.VatZeroRated or TaxKind.NonVat))
                {
                    throw BooksProblem.Invalid($"Line {i}: sales lines take output VAT, zero-rated or non-VAT codes.");
                }
                if (code.Kind != TaxKind.VatOutput && line.TaxOverride is not null and not 0)
                {
                    throw BooksProblem.Invalid($"Line {i}: zero-rated and non-VAT lines have no tax to override.");
                }
                if (!code.AppliesOn(input.Date))
                {
                    issues.Add(new JournalIssue("TAX_CODE_NOT_EFFECTIVE", $"{code.Name} does not apply on {input.Date:yyyy-MM-dd}.", i));
                }
                tax = TaxCalculator.ForLine(net + otherTax, code.Rate, minor, line.TaxOverride, line.TaxReason);
            }
            var salesLine = new SalesInvoiceLine(line.AccountId, line.Description, line.ItemId, line.Quantity, line.UnitPrice, net, line.TaxCodeId, tax,
                line.OtherTaxCodeId, otherTax);
            if (tracked)
            {
                // Average cost (owner default, Manager.io style) of the stock before this document, on its date.
                var average = await Inventory.AverageCostAsync(db, ledger.Id, line.ItemId!.Value, input.Date, invoice.Id, ct);
                salesLine.Costed(Math.Round(line.Quantity!.Value * average, minor, MidpointRounding.AwayFromZero),
                    await Inventory.AccountAsync(db, ledger.Id, Usage.Inventory, ct), await Inventory.AccountAsync(db, ledger.Id, Usage.CostOfSales, ct));
            }
            lines.Add(salesLine);
        }

        if (input.OriginalInvoiceId is { } original
            && !await db.SalesInvoices.AnyAsync(i => i.LedgerId == ledger.Id && i.Id == original && !i.IsCreditNote && i.CustomerId == customer.Id, ct))
        {
            throw BooksProblem.Invalid("original_invoice_id must be a sales invoice of the same customer.");
        }
        var dueDate = input.DueDate ?? input.Date.AddDays(customer.PaymentTermsDays ?? 0);
        invoice.Set(customer.Id, receivable, input.Date, dueDate, input.Reference, input.Description, lines, isNew, input.OriginalInvoiceId);
        if (await taxCurrency.ApplyAsync(ledger, invoice.SourceKind, invoice.Id, invoice.Date, invoice.GrossTotal, invoice.TaxTotal, input.TaxCurrency, ct) is { } rateIssue)
        {
            issues.Add(rateIssue);
        }
        return issues;
    }

    /// <summary>The given account must carry the Receivable usage; when omitted, the ledger must have exactly one.</summary>
    private async Task<Guid> ReceivableAccountAsync(Guid ledgerId, Guid? accountId, CancellationToken ct)
    {
        var receivables = await db.AccountUsages.Where(u => u.LedgerId == ledgerId && u.Usage == Usage.Receivable).Select(u => u.AccountId).ToListAsync(ct);
        if (accountId is { } id)
        {
            return receivables.Contains(id) ? id : throw BooksProblem.Invalid("receivable_account_id must be an account with the Receivable usage.");
        }
        return receivables.Count switch
        {
            1 => receivables[0],
            0 => throw BooksProblem.Invalid("This ledger has no Accounts Receivable account yet; apply the Cambodia template or give an account the Receivable usage."),
            _ => throw BooksProblem.Invalid("This ledger has several Receivable accounts; choose one with receivable_account_id."),
        };
    }

    private async Task<Dictionary<Guid, TaxCode>> TaxCodesAsync(SalesInvoice invoice, CancellationToken ct)
    {
        var ids = invoice.Lines.SelectMany(l => new[] { l.TaxCodeId, l.OtherTaxCodeId }).OfType<Guid>().Distinct().ToList();
        return await db.TaxCodes.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
    }

    private async Task<SalesInvoice> FindAsync(Guid ledgerId, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await db.SalesInvoices.Include(i => i.Lines).SingleOrDefaultAsync(i => i.LedgerId == ledgerId && i.Id == invoiceId, ct)
            ?? throw BooksProblem.NotFound();
        return invoice.Status == DocumentStatus.Deleted ? throw BooksProblem.Conflict("DOCUMENT_DELETED", "This invoice was deleted.") : invoice;
    }

    private async Task<IReadOnlyList<SalesInvoiceDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct)
    {
        var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        static string? Q(decimal? v) => v?.ToString("0.####", CultureInfo.InvariantCulture);
        var rows = await db.SalesInvoices.AsNoTracking().Include(i => i.Lines)
            .Where(i => i.LedgerId == ledgerId && (id == null || i.Id == id))
            .OrderByDescending(i => i.Date).ThenByDescending(i => i.InvoiceNo)
            .Select(i => new { i, JournalNo = db.Journals.Where(j => j.Id == i.JournalId).Select(j => (long?)j.JournalNo).FirstOrDefault() })
            .ToListAsync(ct);
        var taxCurrencies = await taxCurrency.LoadAsync(ledgerId, rows.Select(x => x.i.Id).ToList(), ct);
        return rows.Select(x => new SalesInvoiceDto(
            x.i.Id, x.i.LedgerId, x.i.IsCreditNote ? "credit_note" : "invoice", x.i.InvoiceNo, x.i.OriginalInvoiceId, x.i.CustomerId, x.i.ReceivableAccountId,
            x.i.Date, x.i.DueDate, x.i.Reference, x.i.Description,
            x.i.Lines.OrderBy(l => l.LineNo).Select(l => new SalesLineDto(l.LineId, l.AccountId, l.Description, l.ItemId, Q(l.Quantity), Q(l.UnitPrice),
                F(l.NetAmount), l.TaxCodeId, F(l.TaxCalculated), F(l.TaxAmount), l.TaxAdjusted, l.TaxReason, l.OtherTaxCodeId, F(l.OtherTaxAmount))).ToList(),
            F(x.i.NetTotal), F(x.i.OtherTaxTotal), F(x.i.TaxTotal), F(x.i.GrossTotal), taxCurrencies.GetValueOrDefault(x.i.Id),
            Text.Of(x.i.Status), x.i.Issues is null ? [] : JsonSerializer.Deserialize<List<JournalIssue>>(x.i.Issues, BooksJson.Options)!,
            x.i.JournalId, x.JournalNo, x.i.Version.ToString())).ToList();
    }
}
