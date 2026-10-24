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
    string? WithholdingCategory, decimal Withholding, decimal? KhrRate, string? PostingUsage = null)
{
    public const string JournalKind = "journal";

    public bool IsSale => DocumentKind is SalesInvoice.Kind or SalesInvoice.CreditNoteKind;
    public bool IsPurchase => DocumentKind is PurchaseInvoice.Kind or PurchaseInvoice.DebitNoteKind;
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
    string Key, string Section, string Label, LineRule? Lines = null, FormulaRule? Formula = null, bool Information = false, PostingRule? Postings = null);

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
public sealed record LineRule(
    string Side, [property: JsonPropertyName("tax_kinds")] IReadOnlyList<string>? TaxKinds = null,
    [property: JsonPropertyName("valid_tax_invoice")] bool? ValidTaxInvoice = null,
    [property: JsonPropertyName("withholding_category")] string? WithholdingCategory = null, string Amount = "net");

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
                ? rule.Amount switch { "tax" => l.Tax, "withholding" => l.Withholding, _ => l.Net }
                : null;
    }

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
    private static readonly ReturnDefinitionSpec Cambodia = new("Cambodia monthly tax return (draft boxes)", CambodiaWithholding,
    [
        new("taxable_sales", Vat, "Taxable sales, excluding VAT", new LineRule("sale", ["vat_output"])),
        new("output_vat", Vat, "Output VAT", new LineRule("sale", ["vat_output"], Amount: "tax"), Postings: new PostingRule("output_vat")),
        new("zero_rated_sales", Vat, "Zero-rated sales", new LineRule("sale", ["vat_zero_rated"])),
        new("non_vat_sales", Vat, "Sales outside VAT", new LineRule("sale", ["non_vat", "none"])),
        new("taxable_purchases", Vat, "Purchases with a valid tax invoice, excluding VAT", new LineRule("purchase", ["vat_input"], true)),
        new("input_vat", Vat, "Deductible input VAT", new LineRule("purchase", ["vat_input"], true, Amount: "tax"), Postings: new PostingRule("input_vat")),
        new("net_vat", Vat, "VAT payable (negative: credit)", Formula: new FormulaRule([new("output_vat"), new("input_vat", -1)])),
        new("non_deductible_vat", Vat, "Input VAT without a valid tax invoice (not deductible)",
            new LineRule("purchase", ["vat_input"], false, Amount: "tax"), Information: true),
        new("turnover", Prepayment, "Turnover, excluding VAT", new LineRule("sale")),
        new("prepayment", Prepayment, "Prepayment of tax on income (1% of turnover)", Formula: new FormulaRule([new("turnover")], 1m)),
        .. CambodiaWithholding.SelectMany(c => new BoxSpec[]
        {
            new($"wht_{c.Key}_base", Withholding, $"{c.Label}: amounts paid or accrued", new LineRule("purchase", WithholdingCategory: c.Key)),
            new($"wht_{c.Key}", Withholding, $"{c.Label}: tax withheld", new LineRule("purchase", WithholdingCategory: c.Key, Amount: "withholding")),
        }),
        new("wht_journals", Withholding, "Withholding tax posted by journals", Postings: new PostingRule("withholding_payable")),
        new("wht_total", Withholding, "Withholding tax payable",
            Formula: new FormulaRule([.. CambodiaWithholding.Select(c => new FormulaTerm($"wht_{c.Key}")), new FormulaTerm("wht_journals")])),
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
