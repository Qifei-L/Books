using System.Text.Json.Serialization;
using Books.Domain.Access;
using Books.Domain.Ledgers;

namespace Books.Domain.Documents;

/// <summary>
/// One posted document line as the tax return sees it. Amounts are signed: credit and debit notes are negative.
/// </summary>
/// <param name="KhrRate">The rate of the document's tax currency snapshot (owner decision: the return starts from the snapshots).</param>
/// <param name="PostingUsage">For a journal posting on a tax account (DocumentKind "journal"): the account's usage; its amount is in Tax.</param>
public sealed record TaxLine(
    string DocumentKind, Guid DocumentId, long DocumentNo, DateOnly Date, Guid PartyId, string? Reference, int LineNo, string? Description,
    Guid AccountId, Guid? TaxCodeId, TaxKind? TaxKind, decimal Net, decimal Tax, decimal TaxCalculated, string? TaxReason, bool ValidTaxInvoice,
    string? WithholdingCategory, decimal Withholding, decimal? KhrRate, string? PostingUsage = null, bool PartyTaxable = false, PayRunLine? Payroll = null)
{
    public const string JournalKind = "journal";

    /// <summary>Sales invoices and credit notes, and receipt lines posted straight to an account with a tax code.</summary>
    public bool IsSale => DocumentKind is SalesInvoice.Kind or SalesInvoice.CreditNoteKind or Payment.ReceiptKind;

    /// <summary>Purchase invoices and debit notes, and payment lines posted straight to an account with a tax code.</summary>
    public bool IsPurchase => DocumentKind is PurchaseInvoice.Kind or PurchaseInvoice.DebitNoteKind or Payment.Kind;
    public bool TaxAdjusted => Tax != TaxCalculated;
}

public sealed record WithholdingCategory(string Key, string Label);

/// <summary>
/// A tax pack's monthly return as data (owner decision): versions take effect from a month, administrators publish
/// them, AI clients may draft them. Only this fixed vocabulary is allowed, so a definition can be checked before use.
/// </summary>
public sealed record ReturnDefinitionSpec(
    string Name, [property: JsonPropertyName("withholding_categories")] IReadOnlyList<WithholdingCategory> WithholdingCategories, IReadOnlyList<BoxSpec> Boxes);

/// <summary>
/// A box adds up document lines (Lines) and journal postings on tax accounts (Postings), or is calculated from earlier
/// boxes (Formula).
/// </summary>
public sealed record BoxSpec(
    string Key, string Section, string Label, LineRule? Lines = null, FormulaRule? Formula = null, bool Information = false, PostingRule? Postings = null,
    PayrollRule? Payroll = null);

/// <summary>
/// Employee lines of the month's posted pay runs (payroll results imported from the payroll system).
/// </summary>
/// <param name="Amount">gross, fringe_benefits, salary_tax, fringe_benefit_tax, nssf_employee or nssf_employer.</param>
/// <param name="Resident">Only residents (true) or non-residents (false); omit for both.</param>
public sealed record PayrollRule(string Amount, bool? Resident = null);

/// <summary>
/// Journal postings, not from purchase or sales documents, on the accounts carrying a usage (output_vat, input_vat or
/// withholding_payable): manual tax corrections count in the return and show in its drilldown (owner decision).
/// Liabilities count credits as positive; input VAT counts debits as positive.
/// </summary>
public sealed record PostingRule(string Usage);

/// <param name="Side">sale or purchase.</param>
/// <param name="TaxKinds">vat_output, vat_input, vat_zero_rated, non_vat or none (no tax code); omit for any.</param>
/// <param name="ValidTaxInvoice">Purchases: only lines with (true) or without (false) a valid tax invoice; omit for both.</param>
/// <param name="WithholdingCategory">Only lines whose withholding code has this category; omit for any.</param>
/// <param name="Amount">net, tax or withholding.</param>
/// <param name="Party">taxable (the customer or supplier has a tax ID) or non_taxable; omit for both.</param>
public sealed record LineRule(
    string Side, [property: JsonPropertyName("tax_kinds")] IReadOnlyList<string>? TaxKinds = null,
    [property: JsonPropertyName("valid_tax_invoice")] bool? ValidTaxInvoice = null,
    [property: JsonPropertyName("withholding_category")] string? WithholdingCategory = null, string Amount = "net", string? Party = null);

/// <summary>The sum of earlier boxes, each added or subtracted, optionally times a percent and rounded to the currency.</summary>
public sealed record FormulaRule(IReadOnlyList<FormulaTerm> Terms, decimal? Percent = null);

public sealed record FormulaTerm(string Box, int Sign = 1);

/// <summary>
/// A box of a compiled definition. A line box adds up what each document line contributes (null: not in the box);
/// a formula box is calculated from other boxes, separately in USD and KHR.
/// </summary>
public sealed record ReturnBox(
    string Key, string Section, string Label, Func<TaxLine, decimal?>? Line = null, Func<Func<string, decimal>, int, decimal>? Formula = null,
    bool Information = false);

/// <param name="Version">0 for the tax pack's built-in definition.</param>
/// <param name="EffectiveFrom">First month the version applies to; null for the built-in one.</param>
public sealed record TaxReturnDefinition(
    string TaxPack, int Version, DateOnly? EffectiveFrom, string Name, IReadOnlyList<WithholdingCategory> WithholdingCategories, IReadOnlyList<ReturnBox> Boxes);

public static class TaxReturns
{
    private static readonly Dictionary<string, TaxKind?> Kinds = new()
    {
        ["vat_output"] = TaxKind.VatOutput, ["vat_input"] = TaxKind.VatInput, ["vat_zero_rated"] = TaxKind.VatZeroRated,
        ["non_vat"] = TaxKind.NonVat, ["none"] = null,
    };

    /// <summary>The tax pack's built-in definition, used until a ledger publishes its own version.</summary>
    public static ReturnDefinitionSpec? Builtin(string? taxPack) => taxPack == TaxPacks.Cambodia2026 ? Cambodia : null;

    /// <summary>Checks a definition and turns it into boxes; every problem is reported at once.</summary>
    public static TaxReturnDefinition Compile(string taxPack, int version, DateOnly? effectiveFrom, ReturnDefinitionSpec spec)
    {
        var errors = new List<string>();
        var categories = (spec.WithholdingCategories ?? []).ToList();
        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            errors.Add("name is required.");
        }
        foreach (var dup in categories.GroupBy(c => c.Key).Where(g => g.Count() > 1))
        {
            errors.Add($"Withholding category {dup.Key} appears more than once.");
        }
        var categoryKeys = categories.Select(c => c.Key).ToHashSet();
        var seen = new HashSet<string>();
        var boxes = new List<ReturnBox>();
        foreach (var (box, i) in (spec.Boxes ?? []).Select((b, i) => (b, i + 1)))
        {
            var at = $"Box {i} ({box.Key})";
            if (string.IsNullOrWhiteSpace(box.Key) || !box.Key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            {
                errors.Add($"{at}: key must use lowercase letters, digits and underscores.");
            }
            else if (!seen.Add(box.Key))
            {
                errors.Add($"{at}: key appears more than once.");
            }
            if (string.IsNullOrWhiteSpace(box.Label) || string.IsNullOrWhiteSpace(box.Section))
            {
                errors.Add($"{at}: section and label are required.");
            }
            if (box.Payroll is { } payroll)
            {
                if (box.Lines is not null || box.Postings is not null || box.Formula is not null)
                {
                    errors.Add($"{at}: a payroll box takes no lines, postings or formula.");
                    continue;
                }
                if (!PayrollAmounts.Contains(payroll.Amount))
                {
                    errors.Add($"{at}: payroll amount must be one of {string.Join(", ", PayrollAmounts)}.");
                }
                boxes.Add(new ReturnBox(box.Key, box.Section, box.Label, Payroll(payroll), Information: box.Information));
                continue;
            }
            if ((box.Lines is null && box.Postings is null) == (box.Formula is null))
            {
                errors.Add($"{at}: give lines and/or postings, or a formula.");
                continue;
            }
            if (box.Postings is { } postings && !PostingUsages.Contains(postings.Usage))
            {
                errors.Add($"{at}: postings usage must be one of {string.Join(", ", PostingUsages)}.");
            }
            if (box.Formula is null && box.Lines is null)
            {
                boxes.Add(new ReturnBox(box.Key, box.Section, box.Label, Posting(box.Postings!), Information: box.Information));
            }
            else if (box.Lines is { } rule)
            {
                if (rule.Side is not ("sale" or "purchase"))
                {
                    errors.Add($"{at}: side must be sale or purchase.");
                }
                var unknown = (rule.TaxKinds ?? []).Where(k => !Kinds.ContainsKey(k)).ToList();
                if (unknown.Count > 0)
                {
                    errors.Add($"{at}: unknown tax kinds {string.Join(", ", unknown)}; use {string.Join(", ", Kinds.Keys)}.");
                }
                if (rule.WithholdingCategory is { } category && !categoryKeys.Contains(category))
                {
                    errors.Add($"{at}: withholding category {category} is not defined.");
                }
                if (rule.Amount is not ("net" or "tax" or "withholding"))
                {
                    errors.Add($"{at}: amount must be net, tax or withholding.");
                }
                if (rule.Party is not (null or "taxable" or "non_taxable"))
                {
                    errors.Add($"{at}: party must be taxable or non_taxable.");
                }
                var fromLines = Line(rule);
                var fromPostings = box.Postings is { } extra ? Posting(extra) : null;
                boxes.Add(new ReturnBox(box.Key, box.Section, box.Label, l => fromLines(l) ?? fromPostings?.Invoke(l), Information: box.Information));
            }
            else if (box.Formula is { } formula)
            {
                if (formula.Terms is not { Count: > 0 })
                {
                    errors.Add($"{at}: a formula needs at least one term.");
                    continue;
                }
                foreach (var term in formula.Terms.Where(t => !boxes.Any(b => b.Key == t.Box)))
                {
                    errors.Add($"{at}: the formula uses {term.Box}, which must be a box defined before it.");
                }
                if (formula.Terms.Any(t => t.Sign is not (1 or -1)))
                {
                    errors.Add($"{at}: each term's sign must be 1 or -1.");
                }
                if (formula.Percent is { } p && (p <= 0 || p > 100))
                {
                    errors.Add($"{at}: percent must be above 0 and at most 100.");
                }
                var terms = formula.Terms.ToList();
                var percent = formula.Percent;
                boxes.Add(new ReturnBox(box.Key, box.Section, box.Label, Formula: (value, minor) =>
                {
                    var sum = terms.Sum(t => t.Sign * value(t.Box));
                    return percent is { } pc ? Math.Round(sum * pc / 100m, minor, MidpointRounding.AwayFromZero) : sum;
                }, Information: box.Information));
            }
        }
        if (boxes.Count == 0 && errors.Count == 0)
        {
            errors.Add("A definition needs at least one box.");
        }
        return errors.Count == 0
            ? new TaxReturnDefinition(taxPack, version, effectiveFrom, spec.Name!, categories, boxes)
            : throw new DomainException(string.Join(" ", errors));
    }

    public static readonly string[] PayrollAmounts = ["gross", "fringe_benefits", "salary_tax", "fringe_benefit_tax", "nssf_employee", "nssf_employer"];

    private static Func<TaxLine, decimal?> Payroll(PayrollRule rule) =>
        l => l.Payroll is { } p && (rule.Resident is not { } resident || p.Resident == resident)
            ? rule.Amount switch
            {
                "gross" => p.Gross,
                "fringe_benefits" => p.FringeBenefits,
                "salary_tax" => p.SalaryTax,
                "fringe_benefit_tax" => p.FringeBenefitTax,
                "nssf_employee" => p.NssfEmployee,
                _ => p.NssfEmployer,
            }
            : null;

    /// <summary>The tax account usages a box can take journal postings from.</summary>
    public static readonly string[] PostingUsages = ["output_vat", "input_vat", "withholding_payable"];

    private static Func<TaxLine, decimal?> Posting(PostingRule rule) =>
        l => l.DocumentKind == TaxLine.JournalKind && l.PostingUsage == rule.Usage ? l.Tax : null;

    private static Func<TaxLine, decimal?> Line(LineRule rule)
    {
        var kinds = rule.TaxKinds?.Where(Kinds.ContainsKey).Select(k => Kinds[k]).ToList();
        return l =>
            (rule.Side == "sale" ? l.IsSale : l.IsPurchase)
            && (kinds is null || kinds.Contains(l.TaxKind))
            && (rule.ValidTaxInvoice is not { } valid || l.ValidTaxInvoice == valid)
            && (rule.WithholdingCategory is null || l.WithholdingCategory == rule.WithholdingCategory)
            && (rule.Party is null || (rule.Party == "taxable") == l.PartyTaxable)
                ? rule.Amount switch { "tax" => l.Tax, "withholding" => l.Withholding, _ => l.Net }
                : null;
    }

    /// <summary>Form WT 03: Part I resident (article 25 new) and Part II non-resident (article 26 new) payments.</summary>
    private static readonly WithholdingCategory[] CambodiaWithholding =
    [
        new("r1_services", "WT 03 I.1 Services, royalties for intangibles, interests in minerals (15%)"),
        new("r2_interest", "WT 03 I.2 Interest paid by non-bank or saving institution taxpayers (15%)"),
        new("r3_fixed_deposit", "WT 03 I.3 Interest to taxpayers with fixed term deposit accounts (6%)"),
        new("r4_saving", "WT 03 I.4 Interest to taxpayers with non-fixed term saving (4%)"),
        new("r5_rent", "WT 03 I.5 Rental or lease of movable and immovable property (10%)"),
        new("n1_interest", "WT 03 II.1 Non-resident: interest (14%)"),
        new("n2_royalty_rent", "WT 03 II.2 Non-resident: royalty, rental and income from the use of property (14%)"),
        new("n3_management", "WT 03 II.3 Non-resident: management fee and technical services (14%); the template's single non-resident 14% code reports here"),
        new("n4_dividend", "WT 03 II.4 Non-resident: dividend (14%)"),
    ];

    private const string VatPurchases = "T7 01 B. VAT: monthly purchases";
    private const string VatSales = "T7 01 B. VAT: monthly sales";
    private const string Prepayment = "T7 01 A. Prepayment of tax on income";
    private const string Resident = "WT 03 I. Withholding tax on residents";
    private const string NonResident = "WT 03 II. Withholding tax on non-residents";
    private const string WithholdingTotal = "WT 03 Withholding tax";
    private const string Salary = "TOS 01 Tax on salary";

    private static BoxSpec Wht(WithholdingCategory c, bool tax, string section) =>
        new($"wt03_{c.Key}{(tax ? "" : "_base")}", section, $"{c.Label}: {(tax ? "withholding tax" : "amount to be paid")}",
            new LineRule("purchase", WithholdingCategory: c.Key, Amount: tax ? "withholding" : "net"));

    /// <summary>
    /// The monthly return as on GDT's current forms (TTR001 total return, T7 01 head office, WT 03), checked against
    /// the forms downloaded from tax.gov.kh on 2026-10-24. Box numbers follow the forms. A taxable person is a
    /// customer or supplier with a tax ID. Imports (T7 01 boxes 05-06) come from customs declarations, which Books
    /// does not record yet; credit carried from last month (TTR001 boxes 02 and 05) is entered on the form by hand.
    /// </summary>
    private static readonly ReturnDefinitionSpec Cambodia = new("Cambodia monthly tax return (TTR001, T7 01, WT 03)", CambodiaWithholding,
    [
        new("t701_03", VatPurchases, "03 Local purchases with VAT 10%, excluding VAT", new LineRule("purchase", ["vat_input"], true)),
        new("t701_04", VatPurchases, "04 VAT on local purchases", new LineRule("purchase", ["vat_input"], true, Amount: "tax")),
        new("t701_07", VatPurchases, "07 Purchases without VAT, taxable persons", new LineRule("purchase", ["vat_zero_rated", "non_vat", "none"], Party: "taxable")),
        new("t701_08", VatPurchases, "08 Purchases without VAT, non-taxable persons", new LineRule("purchase", ["vat_zero_rated", "non_vat", "none"], Party: "non_taxable")),
        new("t701_09", VatPurchases, "09 Non-creditable purchases, taxable persons", new LineRule("purchase", ["vat_input"], false, Party: "taxable")),
        new("t701_10", VatPurchases, "10 Non-creditable purchases, non-taxable persons", new LineRule("purchase", ["vat_input"], false, Party: "non_taxable")),
        new("t701_input_journals", VatPurchases, "Input VAT posted by journals (in box 11)", Postings: new PostingRule("input_vat")),
        new("t701_11", VatPurchases, "11 Total input tax (04 + 06)", Formula: new FormulaRule([new("t701_04"), new("t701_input_journals")])),
        new("t701_non_deductible_vat", VatPurchases, "VAT on non-creditable purchases (expensed, not on the form)",
            new LineRule("purchase", ["vat_input"], false, Amount: "tax"), Information: true),
        new("t701_12", VatSales, "12 Sales with VAT 10% to taxable persons, excluding VAT", new LineRule("sale", ["vat_output"], Party: "taxable")),
        new("t701_13", VatSales, "13 VAT on sales to taxable persons", new LineRule("sale", ["vat_output"], Party: "taxable", Amount: "tax")),
        new("t701_14", VatSales, "14 Sales with VAT 10% to non-taxable persons, excluding VAT", new LineRule("sale", ["vat_output"], Party: "non_taxable")),
        new("t701_15", VatSales, "15 VAT on sales to non-taxable persons", new LineRule("sale", ["vat_output"], Party: "non_taxable", Amount: "tax")),
        new("t701_16", VatSales, "16 Exports at 0%", new LineRule("sale", ["vat_zero_rated"])),
        new("t701_17", VatSales, "17 Sales without VAT, taxable persons", new LineRule("sale", ["non_vat", "none"], Party: "taxable")),
        new("t701_18", VatSales, "18 Sales without VAT, non-taxable persons", new LineRule("sale", ["non_vat", "none"], Party: "non_taxable")),
        new("t701_output_journals", VatSales, "Output VAT posted by journals (in box 19)", Postings: new PostingRule("output_vat")),
        new("t701_19", VatSales, "19 Total output tax (13 + 15)", Formula: new FormulaRule([new("t701_13"), new("t701_15"), new("t701_output_journals")])),
        new("t701_vat_payable", VatSales, "VAT to be paid, 19 - 11 (negative: credit forward)", Formula: new FormulaRule([new("t701_19"), new("t701_11", -1)])),
        new("t701_turnover", Prepayment, "Turnover excluding VAT (base for box 01)", new LineRule("sale"), Information: true),
        new("t701_ppi_01", Prepayment, "01 Prepayment of tax on income for the month (1%)", Formula: new FormulaRule([new("t701_turnover")], 1m)),
        .. CambodiaWithholding.Take(5).SelectMany(c => new[] { Wht(c, false, Resident), Wht(c, true, Resident) }),
        new("wt03_resident_total", Resident, "Total withholding tax on residents",
            Formula: new FormulaRule([.. CambodiaWithholding.Take(5).Select(c => new FormulaTerm($"wt03_{c.Key}"))])),
        .. CambodiaWithholding.Skip(5).SelectMany(c => new[] { Wht(c, false, NonResident), Wht(c, true, NonResident) }),
        new("wt03_nonresident_total", NonResident, "Total withholding tax on non-residents",
            Formula: new FormulaRule([.. CambodiaWithholding.Skip(5).Select(c => new FormulaTerm($"wt03_{c.Key}"))])),
        new("wt03_journals", WithholdingTotal, "Withholding tax posted by journals", Postings: new PostingRule("withholding_payable")),
        new("wt03_total", WithholdingTotal, "Withholding tax payable (TTR001 line 03)",
            Formula: new FormulaRule([new("wt03_resident_total"), new("wt03_nonresident_total"), new("wt03_journals")])),
        new("tos_resident_gross", Salary, "Salaries of resident employees", Payroll: new PayrollRule("gross", true)),
        new("tos_resident_tax", Salary, "Tax on salary, resident employees", Payroll: new PayrollRule("salary_tax", true)),
        new("tos_nonresident_gross", Salary, "Salaries of non-resident employees", Payroll: new PayrollRule("gross", false)),
        new("tos_nonresident_tax", Salary, "Tax on salary, non-resident employees", Payroll: new PayrollRule("salary_tax", false)),
        new("tos_fringe_benefits", Salary, "Fringe benefits", Payroll: new PayrollRule("fringe_benefits")),
        new("tos_fringe_benefit_tax", Salary, "Tax on fringe benefits", Payroll: new PayrollRule("fringe_benefit_tax")),
        new("tos_total", Salary, "Tax on salary payable (TTR001 lines 01 and 02)",
            Formula: new FormulaRule([new("tos_resident_tax"), new("tos_nonresident_tax"), new("tos_fringe_benefit_tax")])),
    ]);
}

public enum DefinitionStatus { Draft, Published }

/// <summary>
/// A ledger's version of its return definition. Drafts can change; a published version never does and applies from
/// its effective month until a later version starts (owner decision).
/// </summary>
public sealed class ReturnDefinitionVersion
{
    private ReturnDefinitionVersion() { }

    public ReturnDefinitionVersion(Guid id, Guid orgId, Guid ledgerId, int versionNo, DateOnly effectiveFrom, string definition, Guid createdBy, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        VersionNo = versionNo;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        Set(effectiveFrom, definition);
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public int VersionNo { get; private set; }

    /// <summary>The first day of the first month the version applies to.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    public DefinitionStatus Status { get; private set; } = DefinitionStatus.Draft;

    /// <summary>The definition's JSON (ReturnDefinitionSpec).</summary>
    public string Definition { get; private set; } = "";

    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public long Version { get; private set; } = 1;

    public void Update(DateOnly effectiveFrom, string definition)
    {
        if (Status != DefinitionStatus.Draft)
        {
            throw new DomainException("A published definition cannot change; draft a new version instead.");
        }
        Set(effectiveFrom, definition);
        Version++;
    }

    public void Publish(Guid by, DateTimeOffset at)
    {
        if (Status != DefinitionStatus.Draft)
        {
            throw new DomainException("This version is already published.");
        }
        Status = DefinitionStatus.Published;
        PublishedBy = by;
        PublishedAt = at;
        Version++;
    }

    private void Set(DateOnly effectiveFrom, string definition)
    {
        if (effectiveFrom.Day != 1)
        {
            throw new DomainException("effective_from must be a month.");
        }
        EffectiveFrom = effectiveFrom;
        Definition = definition;
    }
}

/// <summary>
/// A filed monthly return: the return as it was when filed, kept unchanged (owner decision). Filing also closes the
/// ledger through the month end, so the month's documents cannot change afterwards unless an administrator reopens it.
/// </summary>
public sealed class FiledTaxReturn
{
    private FiledTaxReturn() { }

    public FiledTaxReturn(Guid id, Guid orgId, Guid ledgerId, DateOnly period, string snapshot, Guid filedBy, DateTimeOffset filedAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        Period = period;
        Snapshot = snapshot;
        FiledBy = filedBy;
        FiledAt = filedAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Period { get; private set; }

    /// <summary>The return's JSON when filed.</summary>
    public string Snapshot { get; private set; } = "";

    public Guid FiledBy { get; private set; }
    public DateTimeOffset FiledAt { get; private set; }
}
