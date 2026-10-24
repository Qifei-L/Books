using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;

namespace Books.Domain.Documents;

/// <summary>
/// A supplier's invoice. Saved through the document framework: it and its journal change together (rule 31),
/// and the journal changes only through the invoice (rule 32). Totals are derived from the lines, never stored.
/// </summary>
public sealed class PurchaseInvoice
{
    public const string Kind = "purchase_invoice";
    public const string DebitNoteKind = "purchase_debit_note";

    private readonly List<PurchaseInvoiceLine> _lines = [];

    private PurchaseInvoice() { }

    public PurchaseInvoice(Guid id, Guid orgId, Guid ledgerId, long invoiceNo, Guid createdBy, DateTimeOffset createdAt, bool isDebitNote = false)
    {
        IsDebitNote = isDebitNote;
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        InvoiceNo = invoiceNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>System number per ledger; the supplier's own invoice number goes in Reference.</summary>
    public long InvoiceNo { get; private set; }

    public Guid SupplierId { get; private set; }

    /// <summary>
    /// A debit note: the supplier's credit for returns or allowances. Same lines, opposite posting: it reduces the
    /// expense, the VAT and the payable. Numbered in its own series.
    /// </summary>
    public bool IsDebitNote { get; private set; }

    /// <summary>For a debit note, the invoice it corrects (optional, same supplier).</summary>
    public Guid? OriginalInvoiceId { get; private set; }

    public string SourceKind => IsDebitNote ? DebitNoteKind : Kind;

    /// <summary>An active control account carrying the Payable usage (Account Usage, owner decision).</summary>
    public Guid PayableAccountId { get; private set; }

    public DateOnly Date { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string? Reference { get; private set; }
    public string? Description { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public string? Issues { get; private set; }
    public Guid? JournalId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public IReadOnlyList<PurchaseInvoiceLine> Lines => _lines;

    public decimal NetTotal => _lines.Sum(l => l.NetAmount);
    public decimal TaxTotal => _lines.Sum(l => l.TaxAmount);
    public decimal WithholdingTotal => _lines.Sum(l => l.WithholdingAmount);

    /// <summary>What the supplier invoiced, VAT included.</summary>
    public decimal GrossTotal => NetTotal + TaxTotal;

    /// <summary>What is owed to the supplier: gross less withholding tax, which is paid to the tax office instead.</summary>
    public decimal PayableTotal => GrossTotal - WithholdingTotal;

    public void Set(Guid supplierId, Guid payableAccountId, DateOnly date, DateOnly dueDate, string? reference, string? description,
        IEnumerable<PurchaseInvoiceLine> lines, bool isNew, Guid? originalInvoiceId = null)
    {
        if (originalInvoiceId is not null && !IsDebitNote)
        {
            throw new DomainException("Only a debit note refers to an original invoice.");
        }
        OriginalInvoiceId = originalInvoiceId;
        if (dueDate < date)
        {
            throw new DomainException("due_date must not be before the invoice date.");
        }
        var newLines = lines.ToList();
        if (newLines.Count is 0 or > 500)
        {
            throw new DomainException("An invoice needs 1–500 lines.");
        }
        SupplierId = supplierId;
        PayableAccountId = payableAccountId;
        Date = date;
        DueDate = dueDate;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        _lines.Clear();
        _lines.AddRange(newLines.Select((l, i) => l.Numbered(Id, i + 1)));
        if (!isNew)
        {
            Version++;
        }
    }

    public void PostedAs(Guid journalId)
    {
        JournalId = journalId;
        Status = DocumentStatus.Posted;
        Issues = null;
    }

    public void KeptAsDraft(string? issues) => Issues = issues;

    public void Delete()
    {
        Status = DocumentStatus.Deleted;
        Version++;
    }

    /// <summary>
    /// Posting adapter. Dr each line's account with its net amount. VAT: Dr the code's input VAT account when a valid
    /// tax invoice was received, otherwise Dr the code's non-deductible account (owner decision); summed by account.
    /// Cr withholding tax payable, summed by account. Cr Accounts Payable for gross less withholding, naming the supplier.
    /// </summary>
    public JournalDraft ToJournal(IReadOnlyDictionary<Guid, TaxCode> taxCodes)
    {
        // A debit note posts every amount with the opposite sign.
        var sign = IsDebitNote ? -1 : 1;
        var text = Description ?? (IsDebitNote ? $"Debit note {InvoiceNo}" : $"Purchase invoice {InvoiceNo}");
        var lines = _lines.Select(l => new DraftLine(l.AccountId, sign * l.NetAmount, l.Description ?? text, l.LineId, ItemId: l.ItemId, Quantity: l.Quantity)).ToList();
        foreach (var vat in _lines.Where(l => l.TaxAmount != 0 && l.TaxCodeId is not null)
                     .GroupBy(l => VatAccount(taxCodes[l.TaxCodeId!.Value], l.ValidTaxInvoice)))
        {
            lines.Add(new DraftLine(vat.Key, sign * vat.Sum(l => l.TaxAmount), text));
        }
        foreach (var wht in _lines.Where(l => l.WithholdingAmount != 0).GroupBy(l => taxCodes[l.WithholdingCodeId!.Value].AccountId!.Value))
        {
            lines.Add(new DraftLine(wht.Key, -sign * wht.Sum(l => l.WithholdingAmount), $"{text} (withholding)"));
        }
        lines.Add(new DraftLine(PayableAccountId, -sign * PayableTotal, text, PartyId: SupplierId));
        return new JournalDraft(Date, lines, Reference, text);
    }

    private static Guid VatAccount(TaxCode code, bool validTaxInvoice) =>
        validTaxInvoice || code.NonDeductibleAccountId is null ? code.AccountId!.Value : code.NonDeductibleAccountId.Value;
}

/// <summary>
/// One invoice line. Tax and withholding are calculated per line and rounded per line (rule 34); VAT may be
/// overridden with an optional reason, keeping the calculated amount (owner decision).
/// </summary>
public sealed class PurchaseInvoiceLine
{
    private PurchaseInvoiceLine() { }

    public PurchaseInvoiceLine(
        Guid accountId, string? description, Guid? itemId, decimal? quantity, decimal? unitPrice, decimal netAmount,
        Guid? taxCodeId, LineTax? tax, bool validTaxInvoice, Guid? withholdingCodeId, decimal withholdingAmount, Guid? lineId = null)
    {
        LineId = lineId ?? Guid.CreateVersion7();
        AccountId = accountId;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        ItemId = itemId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        NetAmount = netAmount;
        TaxCodeId = taxCodeId;
        TaxCalculated = tax?.Calculated ?? 0;
        TaxAmount = tax?.Amount ?? 0;
        TaxReason = tax?.Reason;
        ValidTaxInvoice = validTaxInvoice;
        WithholdingCodeId = withholdingCodeId;
        WithholdingAmount = withholdingAmount;
    }

    public Guid InvoiceId { get; private set; }
    public int LineNo { get; private set; }
    public Guid LineId { get; private set; }
    public Guid AccountId { get; private set; }
    public string? Description { get; private set; }
    public Guid? ItemId { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public decimal NetAmount { get; private set; }
    public Guid? TaxCodeId { get; private set; }
    public decimal TaxCalculated { get; private set; }
    public decimal TaxAmount { get; private set; }
    public string? TaxReason { get; private set; }
    public bool ValidTaxInvoice { get; private set; }
    public Guid? WithholdingCodeId { get; private set; }
    public decimal WithholdingAmount { get; private set; }

    /// <summary>True when a person entered a VAT amount other than the calculated one; reported by the tax module.</summary>
    public bool TaxAdjusted => TaxAmount != TaxCalculated;

    internal PurchaseInvoiceLine Numbered(Guid invoiceId, int lineNo)
    {
        InvoiceId = invoiceId;
        LineNo = lineNo;
        return this;
    }
}
