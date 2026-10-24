using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;

namespace Books.Domain.Documents;

/// <summary>
/// Money paid out of (payment) or received into (receipt) a bank or cash account, Manager.io style: one document
/// with many lines (owner decision). A line settles an invoice, goes to the party's balance (settled oldest invoice
/// first), or posts straight to an account with optional VAT and withholding; negative lines handle fees, discounts
/// and tax withheld by customers. Totals are derived from the lines, never stored.
/// </summary>
public sealed class Payment
{
    public const string Kind = "payment";
    public const string ReceiptKind = "receipt";

    private readonly List<PaymentLine> _lines = [];

    private Payment() { }

    public Payment(Guid id, Guid orgId, Guid ledgerId, long paymentNo, Guid createdBy, DateTimeOffset createdAt, bool isReceipt = false)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        PaymentNo = paymentNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        IsReceipt = isReceipt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>System number per ledger, in separate series for payments and receipts.</summary>
    public long PaymentNo { get; private set; }

    public bool IsReceipt { get; private set; }

    public string SourceKind => IsReceipt ? ReceiptKind : Kind;

    /// <summary>The bank or cash account money leaves or arrives in (an account with the Bank usage).</summary>
    public Guid BankAccountId { get; private set; }

    /// <summary>The supplier or customer; required for invoice and party-balance lines.</summary>
    public Guid? PartyId { get; private set; }

    public DateOnly Date { get; private set; }
    public string? Reference { get; private set; }
    public string? Description { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public string? Issues { get; private set; }
    public Guid? JournalId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public IReadOnlyList<PaymentLine> Lines => _lines;

    /// <summary>What leaves or arrives in the bank: the sum of the lines' totals.</summary>
    public decimal Total => _lines.Sum(l => l.Total);

    public void Set(Guid bankAccountId, Guid? partyId, DateOnly date, string? reference, string? description, IEnumerable<PaymentLine> lines, bool isNew)
    {
        var newLines = lines.ToList();
        if (newLines.Count is 0 or > 500)
        {
            throw new DomainException("A payment needs 1–500 lines.");
        }
        if (partyId is null && newLines.Any(l => l.Kind != PaymentLineKind.Account))
        {
            throw new DomainException("Invoice and party-balance lines need party_id on the document.");
        }
        if (newLines.Sum(l => l.Total) <= 0)
        {
            throw new DomainException("The total must be more than zero; record money going the other way as a " + (IsReceipt ? "payment." : "receipt."));
        }
        BankAccountId = bankAccountId;
        PartyId = partyId;
        Date = date;
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
    /// Posting adapter. A payment debits what it pays and credits the bank; a receipt the opposite. Invoice and
    /// party-balance lines post to the control account naming the party; account lines post their net amount, VAT
    /// (input VAT or non-deductible for payments, output VAT for receipts) and, on payments, withholding tax payable.
    /// </summary>
    public JournalDraft ToJournal(IReadOnlyDictionary<Guid, TaxCode> taxCodes)
    {
        var sign = IsReceipt ? -1 : 1;
        var text = Description ?? (IsReceipt ? $"Receipt {PaymentNo}" : $"Payment {PaymentNo}");
        var lines = new List<DraftLine>();
        foreach (var l in _lines)
        {
            if (l.Kind == PaymentLineKind.Account)
            {
                lines.Add(new DraftLine(l.AccountId!.Value, sign * l.NetAmount, l.Description ?? text, l.LineId, PartyId, l.ItemId, l.Quantity));
            }
            else
            {
                lines.Add(new DraftLine(l.ControlAccountId!.Value, sign * l.NetAmount, l.Description ?? text, l.LineId, PartyId));
            }
        }
        foreach (var vat in _lines.Where(l => l.TaxAmount != 0 && l.TaxCodeId is not null).GroupBy(l => VatAccount(taxCodes[l.TaxCodeId!.Value], l.ValidTaxInvoice)))
        {
            lines.Add(new DraftLine(vat.Key, sign * vat.Sum(l => l.TaxAmount), text));
        }
        foreach (var wht in _lines.Where(l => l.WithholdingAmount != 0).GroupBy(l => taxCodes[l.WithholdingCodeId!.Value].AccountId!.Value))
        {
            lines.Add(new DraftLine(wht.Key, -sign * wht.Sum(l => l.WithholdingAmount), $"{text} (withholding)"));
        }
        lines.Add(new DraftLine(BankAccountId, -sign * Total, text, PartyId: PartyId));
        return new JournalDraft(Date, lines, Reference, text);
    }

    private static Guid VatAccount(TaxCode code, bool validTaxInvoice) =>
        validTaxInvoice || code.NonDeductibleAccountId is null ? code.AccountId!.Value : code.NonDeductibleAccountId.Value;
}

public enum PaymentLineKind { Invoice, Party, Account }

/// <summary>
/// One line of a payment or receipt. Invoice lines settle a named invoice; party lines go to the party's balance;
/// account lines post to an account with VAT and withholding calculated per line (rule 34). Amounts may be negative.
/// </summary>
public sealed class PaymentLine
{
    private PaymentLine() { }

    private PaymentLine(PaymentLineKind kind, string? description, decimal amount)
    {
        LineId = Guid.CreateVersion7();
        Kind = kind;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        NetAmount = amount;
        ValidTaxInvoice = true;
    }

    public static PaymentLine ForInvoice(Guid invoiceId, Guid controlAccountId, string? description, decimal amount) =>
        new(PaymentLineKind.Invoice, description, amount) { InvoiceId = invoiceId, ControlAccountId = controlAccountId };

    public static PaymentLine ForParty(Guid controlAccountId, string? description, decimal amount) =>
        new(PaymentLineKind.Party, description, amount) { ControlAccountId = controlAccountId };

    public static PaymentLine ForAccount(
        Guid accountId, string? description, Guid? itemId, decimal? quantity, decimal? unitPrice, decimal netAmount,
        Guid? taxCodeId, LineTax? tax, bool validTaxInvoice, Guid? withholdingCodeId, decimal withholdingAmount) =>
        new(PaymentLineKind.Account, description, netAmount)
        {
            AccountId = accountId, ItemId = itemId, Quantity = quantity, UnitPrice = unitPrice, TaxCodeId = taxCodeId,
            TaxCalculated = tax?.Calculated ?? 0, TaxAmount = tax?.Amount ?? 0, TaxReason = tax?.Reason, ValidTaxInvoice = validTaxInvoice,
            WithholdingCodeId = withholdingCodeId, WithholdingAmount = withholdingAmount,
        };

    public Guid PaymentId { get; private set; }
    public int LineNo { get; private set; }
    public Guid LineId { get; private set; }
    public PaymentLineKind Kind { get; private set; }

    /// <summary>Invoice lines: the purchase (payments) or sales (receipts) invoice settled.</summary>
    public Guid? InvoiceId { get; private set; }

    /// <summary>Invoice and party lines: the Payable (payments) or Receivable (receipts) account.</summary>
    public Guid? ControlAccountId { get; private set; }

    /// <summary>Account lines: where the net amount posts.</summary>
    public Guid? AccountId { get; private set; }

    public string? Description { get; private set; }
    public Guid? ItemId { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? UnitPrice { get; private set; }

    /// <summary>The line's amount: VAT excluded on account lines.</summary>
    public decimal NetAmount { get; private set; }

    public Guid? TaxCodeId { get; private set; }
    public decimal TaxCalculated { get; private set; }
    public decimal TaxAmount { get; private set; }
    public string? TaxReason { get; private set; }
    public bool ValidTaxInvoice { get; private set; }
    public Guid? WithholdingCodeId { get; private set; }
    public decimal WithholdingAmount { get; private set; }

    public bool TaxAdjusted => TaxAmount != TaxCalculated;

    /// <summary>What the line moves through the bank: net plus VAT less withholding (withheld tax is paid to the tax office instead).</summary>
    public decimal Total => NetAmount + TaxAmount - WithholdingAmount;

    internal PaymentLine Numbered(Guid paymentId, int lineNo)
    {
        PaymentId = paymentId;
        LineNo = lineNo;
        return this;
    }
}
