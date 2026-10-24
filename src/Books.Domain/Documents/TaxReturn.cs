using Books.Domain.Ledgers;

namespace Books.Domain.Documents;

/// <summary>
/// One posted document line as the tax return sees it. Amounts are signed: credit and debit notes are negative.
/// </summary>
/// <param name="KhrRate">The rate of the document's tax currency snapshot (owner decision: the return starts from the snapshots).</param>
public sealed record TaxLine(
    string DocumentKind, Guid DocumentId, long DocumentNo, DateOnly Date, Guid PartyId, string? Reference, int LineNo, string? Description,
    Guid AccountId, Guid? TaxCodeId, TaxKind? TaxKind, decimal Net, decimal Tax, decimal TaxCalculated, string? TaxReason, bool ValidTaxInvoice,
    string? WithholdingCategory, decimal Withholding, decimal? KhrRate)
{
    public bool IsSale => DocumentKind is SalesInvoice.Kind or SalesInvoice.CreditNoteKind;
    public bool IsPurchase => !IsSale;
    public bool TaxAdjusted => Tax != TaxCalculated;
}

public sealed record WithholdingCategory(string Key, string Label);

/// <summary>
/// A box of the monthly return. A line box adds up what each document line contributes (null: not in the box);
/// a formula box is calculated from other boxes, separately in USD and KHR.
/// </summary>
public sealed record ReturnBox(
    string Key, string Section, string Label, Func<TaxLine, decimal?>? Line = null, Func<Func<string, decimal>, int, decimal>? Formula = null,
    bool Information = false);

/// <summary>A tax pack's monthly return: its boxes and the categories withholding codes are reported under.</summary>
public sealed record TaxReturnDefinition(string TaxPack, string Name, IReadOnlyList<WithholdingCategory> WithholdingCategories, IReadOnlyList<ReturnBox> Boxes);

public static class TaxReturns
{
    public static TaxReturnDefinition? For(string? taxPack) => taxPack == TaxPacks.Cambodia2026 ? Cambodia : null;

    private static readonly WithholdingCategory[] CambodiaWithholding =
    [
        new("services", "Resident: services, interest, royalties (15%)"),
        new("rent", "Resident: rent of movable and immovable property (10%)"),
        new("non_resident", "Non-resident (14%)"),
    ];

    private const string Vat = "VAT";
    private const string Prepayment = "Prepayment of tax on income";
    private const string Withholding = "Withholding tax";

    /// <summary>
    /// Drafted from 04-cambodia-tax; the boxes and their wording are to be confirmed with an adviser against the
    /// current monthly return form. Keys are stable so a filed return can be compared with a later recalculation.
    /// </summary>
    private static readonly TaxReturnDefinition Cambodia = new(TaxPacks.Cambodia2026, "Cambodia monthly tax return (draft boxes)", CambodiaWithholding,
    [
        new("taxable_sales", Vat, "Taxable sales, excluding VAT", l => l.IsSale && l.TaxKind == TaxKind.VatOutput ? l.Net : null),
        new("output_vat", Vat, "Output VAT", l => l.IsSale && l.TaxKind == TaxKind.VatOutput ? l.Tax : null),
        new("zero_rated_sales", Vat, "Zero-rated sales", l => l.IsSale && l.TaxKind == TaxKind.VatZeroRated ? l.Net : null),
        new("non_vat_sales", Vat, "Sales outside VAT", l => l.IsSale && l.TaxKind is null or TaxKind.NonVat ? l.Net : null),
        new("taxable_purchases", Vat, "Purchases with a valid tax invoice, excluding VAT", l => l.IsPurchase && l.TaxKind == TaxKind.VatInput && l.ValidTaxInvoice ? l.Net : null),
        new("input_vat", Vat, "Deductible input VAT", l => l.IsPurchase && l.TaxKind == TaxKind.VatInput && l.ValidTaxInvoice ? l.Tax : null),
        new("net_vat", Vat, "VAT payable (negative: credit)", Formula: (box, _) => box("output_vat") - box("input_vat")),
        new("non_deductible_vat", Vat, "Input VAT without a valid tax invoice (not deductible)",
            l => l.IsPurchase && l.TaxKind == TaxKind.VatInput && !l.ValidTaxInvoice ? l.Tax : null, Information: true),
        new("turnover", Prepayment, "Turnover, excluding VAT", l => l.IsSale ? l.Net : null),
        new("prepayment", Prepayment, "Prepayment of tax on income (1% of turnover)",
            Formula: (box, minor) => Math.Round(box("turnover") * 0.01m, minor, MidpointRounding.AwayFromZero)),
        .. CambodiaWithholding.SelectMany(c => new ReturnBox[]
        {
            new($"wht_{c.Key}_base", Withholding, $"{c.Label}: amounts paid or accrued", l => l.IsPurchase && l.WithholdingCategory == c.Key ? l.Net : null),
            new($"wht_{c.Key}", Withholding, $"{c.Label}: tax withheld", l => l.IsPurchase && l.WithholdingCategory == c.Key ? l.Withholding : null),
        }),
        new("wht_total", Withholding, "Withholding tax payable",
            Formula: (box, _) => CambodiaWithholding.Sum(c => box($"wht_{c.Key}"))),
    ]);
}
