using Books.Domain.Access;
using Books.Domain.Ledgers;

namespace Books.Domain.Documents;

/// <summary>
/// A tax pack's rule for showing a document's totals in a second, tax currency (Cambodia: KHR on USD invoices).
/// Documents do not know the currency; they offer the slot and the ledger's tax pack fills it (owner decision).
/// </summary>
public interface ITaxCurrencySlot
{
    string Currency { get; }

    bool AppliesTo(string documentKind, string baseCurrency);

    /// <summary>The date whose rate the document uses.</summary>
    DateOnly RateDate(DateOnly documentDate);
}

public static class TaxPacks
{
    public const string Cambodia2026 = "cambodia-2026";

    public static ITaxCurrencySlot? TaxCurrency(string? taxPack) => taxPack == Cambodia2026 ? CambodiaKhr.Instance : null;

    /// <summary>
    /// Self-assessment taxpayers show the invoice total including VAT in KHR, at the NBC rate of the invoice date
    /// (04-cambodia-tax). Lines may stay in USD.
    /// </summary>
    private sealed class CambodiaKhr : ITaxCurrencySlot
    {
        public static readonly CambodiaKhr Instance = new();

        private static readonly HashSet<string> Kinds = [PurchaseInvoice.Kind, PurchaseInvoice.DebitNoteKind, "sales_invoice", "sales_credit_note"];

        public string Currency => "KHR";

        public bool AppliesTo(string documentKind, string baseCurrency) => baseCurrency == "USD" && Kinds.Contains(documentKind);

        public DateOnly RateDate(DateOnly documentDate) => documentDate;
    }
}

/// <summary>
/// The tax currency snapshot kept with a document: the rate used and the totals converted with it. Later changes to
/// the rate table do not touch it; saving the document again recalculates it. Never posted to the ledger.
/// </summary>
public sealed class DocumentTaxCurrency
{
    private DocumentTaxCurrency() { }

    public DocumentTaxCurrency(Guid documentId, Guid ledgerId, string documentKind)
    {
        DocumentId = documentId;
        LedgerId = ledgerId;
        DocumentKind = documentKind;
    }

    public Guid DocumentId { get; private set; }
    public Guid LedgerId { get; private set; }
    public string DocumentKind { get; private set; } = "";
    public string Currency { get; private set; } = "";
    public decimal Rate { get; private set; }
    public DateOnly RateDate { get; private set; }

    /// <summary>The rate table's source (for example NBC or Manual), or Override when entered on the document.</summary>
    public string RateSource { get; private set; } = "";

    public string? Reason { get; private set; }
    public decimal GrossTotal { get; private set; }
    public decimal TaxTotal { get; private set; }

    public void Set(string currency, decimal rate, DateOnly rateDate, string rateSource, string? reason, decimal gross, decimal tax)
    {
        if (rate <= 0)
        {
            throw new DomainException("The tax currency rate must be positive.");
        }
        var minor = Currencies.Require(currency).MinorUnits;
        Currency = currency;
        Rate = rate;
        RateDate = rateDate;
        RateSource = rateSource;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        GrossTotal = Math.Round(gross * rate, minor, MidpointRounding.AwayFromZero);
        TaxTotal = Math.Round(tax * rate, minor, MidpointRounding.AwayFromZero);
    }
}
