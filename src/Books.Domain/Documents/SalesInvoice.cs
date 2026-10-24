using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;

namespace Books.Domain.Documents;

/// <summary>
/// An invoice to a customer. Saved through the document framework like purchase invoices: it and its journal
/// change together (rule 31), and the journal changes only through the invoice (rule 32). Totals are derived.
/// </summary>
public sealed class SalesInvoice
{
    public const string Kind = "sales_invoice";
    public const string CreditNoteKind = "sales_credit_note";

    private readonly List<SalesInvoiceLine> _lines = [];

    private SalesInvoice() { }

    public SalesInvoice(Guid id, Guid orgId, Guid ledgerId, long invoiceNo, Guid createdBy, DateTimeOffset createdAt, bool isCreditNote = false)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        InvoiceNo = invoiceNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        IsCreditNote = isCreditNote;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>System number per ledger, in separate series for invoices and credit notes; the user's own number goes in Reference.</summary>
    public long InvoiceNo { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>A credit note: the customer's credit for returns or allowances. Same lines, opposite posting.</summary>
    public bool IsCreditNote { get; private set; }

    /// <summary>For a credit note, the invoice it corrects (optional, same customer).</summary>
    public Guid? OriginalInvoiceId { get; private set; }

    public string SourceKind => IsCreditNote ? CreditNoteKind : Kind;

    /// <summary>An active control account carrying the Receivable usage (Account Usage, owner decision).</summary>
    public Guid ReceivableAccountId { get; private set; }

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


    /// <summary>The document's currency; amounts on it and its lines are in this currency (M12).</summary>
    public string Currency { get; private set; } = "";

    /// <summary>Units of the document's currency per one unit of the ledger's currency (for example 4100 KHR per USD); null in the ledger's currency.</summary>
    public decimal? ExchangeRate { get; private set; }

    /// <summary>What the posting put on the control account, in the ledger's currency; aging and statements use it.</summary>
    public decimal BaseTotal { get; private set; }

    public void SetCurrency(string currency, decimal? rate, string baseCurrency)
    {
        currency = Currencies.Require(currency).Code;
        if (currency != baseCurrency && rate is not > 0)
        {
            throw new DomainException($"A document in {currency} needs its exchange rate to {baseCurrency}.");
        }
        Currency = currency;
        ExchangeRate = currency == baseCurrency ? null : rate;
    }

    /// <summary>An amount in the ledger's currency, rounded per amount so the journal and the tax return agree.</summary>
    public decimal ToBase(decimal amount, int minorUnits) =>
        ExchangeRate is { } rate ? Math.Round(amount / rate, minorUnits, MidpointRounding.AwayFromZero) : amount;

    public IReadOnlyList<SalesInvoiceLine> Lines => _lines;

    public decimal NetTotal => _lines.Sum(l => l.NetAmount);
    public decimal TaxTotal => _lines.Sum(l => l.TaxAmount);

    /// <summary>Specific, accommodation and public lighting tax charged on the lines.</summary>
    public decimal OtherTaxTotal => _lines.Sum(l => l.OtherTaxAmount);

    /// <summary>What the customer owes, VAT and other taxes included.</summary>
    public decimal GrossTotal => NetTotal + OtherTaxTotal + TaxTotal;

    public void Set(Guid customerId, Guid receivableAccountId, DateOnly date, DateOnly dueDate, string? reference, string? description,
        IEnumerable<SalesInvoiceLine> lines, bool isNew, Guid? originalInvoiceId = null)
    {
        if (originalInvoiceId is not null && !IsCreditNote)
        {
            throw new DomainException("Only a credit note refers to an original invoice.");
        }
        if (dueDate < date)
        {
            throw new DomainException("due_date must not be before the invoice date.");
        }
        var newLines = lines.ToList();
        if (newLines.Count is 0 or > 500)
        {
            throw new DomainException("An invoice needs 1–500 lines.");
        }
        OriginalInvoiceId = originalInvoiceId;
        CustomerId = customerId;
        ReceivableAccountId = receivableAccountId;
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
    /// Posting adapter. Cr each line's account with its net amount; Cr each output VAT account with its VAT, summed by
    /// account; Dr Accounts Receivable with the gross total, naming the customer. A credit note posts the opposite.
    /// </summary>
    /// <remarks>
    /// In another currency each amount converts at the document's rate, line by line (cost of sales is already in the
    /// ledger's currency); the receivable takes the rounding, and what it gets is kept as <see cref="BaseTotal"/>.
    /// </remarks>
    public JournalDraft ToJournal(IReadOnlyDictionary<Guid, TaxCode> taxCodes, int minorUnits)
    {
        var sign = IsCreditNote ? -1 : 1;
        decimal C(decimal amount) => ToBase(amount, minorUnits);
        var text = Description ?? (IsCreditNote ? $"Credit note {InvoiceNo}" : $"Sales invoice {InvoiceNo}");
        var lines = _lines.Select(l => new DraftLine(l.AccountId, -sign * C(l.NetAmount), l.Description ?? text, l.LineId, ItemId: l.ItemId, Quantity: l.Quantity)).ToList();
        foreach (var vat in _lines.Where(l => l.TaxAmount != 0 && l.TaxCodeId is not null).GroupBy(l => taxCodes[l.TaxCodeId!.Value].AccountId!.Value))
        {
            lines.Add(new DraftLine(vat.Key, -sign * vat.Sum(l => C(l.TaxAmount)), text));
        }
        foreach (var cost in _lines.Where(l => l.CostAmount != 0))
        {
            lines.Add(new DraftLine(cost.CostOfSalesAccountId!.Value, sign * cost.CostAmount, $"{text} (cost of sales)", ItemId: cost.ItemId, Quantity: cost.Quantity));
            lines.Add(new DraftLine(cost.InventoryAccountId!.Value, -sign * cost.CostAmount, $"{text} (inventory)", ItemId: cost.ItemId, Quantity: cost.Quantity));
        }
        foreach (var other in _lines.Where(l => l.OtherTaxAmount != 0).GroupBy(l => taxCodes[l.OtherTaxCodeId!.Value].AccountId!.Value))
        {
            lines.Add(new DraftLine(other.Key, -sign * other.Sum(l => C(l.OtherTaxAmount)), $"{text} (other tax)"));
        }
        var receivable = -lines.Sum(l => l.Amount);
        BaseTotal = sign * receivable;
        lines.Add(new DraftLine(ReceivableAccountId, receivable, text, PartyId: CustomerId));
        return new JournalDraft(Date, lines, Reference, text);
    }
}

/// <summary>One sales line. VAT is calculated and rounded per line (rule 34) and may be overridden with an optional reason.</summary>
public sealed class SalesInvoiceLine
{
    private SalesInvoiceLine() { }

    public SalesInvoiceLine(Guid accountId, string? description, Guid? itemId, decimal? quantity, decimal? unitPrice, decimal netAmount, Guid? taxCodeId, LineTax? tax,
        Guid? otherTaxCodeId = null, decimal otherTaxAmount = 0)
    {
        OtherTaxCodeId = otherTaxCodeId;
        OtherTaxAmount = otherTaxAmount;
        LineId = Guid.CreateVersion7();
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

    /// <summary>A specific, accommodation or public lighting tax code; its tax is part of the VAT base.</summary>
    public Guid? OtherTaxCodeId { get; private set; }

    public decimal OtherTaxAmount { get; private set; }

    /// <summary>Inventory items: the quantity at average cost, moved from inventory to cost of sales.</summary>
    public decimal CostAmount { get; private set; }

    public Guid? InventoryAccountId { get; private set; }
    public Guid? CostOfSalesAccountId { get; private set; }

    public void Costed(decimal cost, Guid inventoryAccountId, Guid costOfSalesAccountId)
    {
        CostAmount = cost;
        InventoryAccountId = inventoryAccountId;
        CostOfSalesAccountId = costOfSalesAccountId;
    }

    public bool TaxAdjusted => TaxAmount != TaxCalculated;

    internal SalesInvoiceLine Numbered(Guid invoiceId, int lineNo)
    {
        InvoiceId = invoiceId;
        LineNo = lineNo;
        return this;
    }
}
