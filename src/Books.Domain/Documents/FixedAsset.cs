using Books.Domain.Access;
using Books.Domain.Journals;

namespace Books.Domain.Documents;

/// <summary>
/// A fixed asset in the register (M10, owner default: straight-line depreciation by month). The purchase itself
/// posts through a purchase invoice or payment to the asset account; the register holds what depreciation needs.
/// </summary>
public sealed class FixedAsset
{
    public const string DisposalKind = "asset_disposal";

    private FixedAsset() { }

    public FixedAsset(Guid id, Guid orgId, Guid ledgerId, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public DateOnly AcquisitionDate { get; private set; }
    public decimal Cost { get; private set; }
    public decimal ResidualValue { get; private set; }
    public int UsefulLifeMonths { get; private set; }
    public Guid AssetAccountId { get; private set; }
    public Guid AccumulatedDepreciationAccountId { get; private set; }
    public Guid DepreciationExpenseAccountId { get; private set; }
    public DateOnly? DisposedOn { get; private set; }
    public Guid? DisposalJournalId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public long Version { get; private set; } = 1;

    /// <summary>Depreciation for one full month: (cost - residual value) / useful life, before rounding.</summary>
    public decimal Monthly => (Cost - ResidualValue) / UsefulLifeMonths;

    public void Set(string code, string name, DateOnly acquisitionDate, decimal cost, decimal residualValue, int usefulLifeMonths,
        Guid assetAccountId, Guid accumulatedDepreciationAccountId, Guid depreciationExpenseAccountId, bool isNew)
    {
        if (DisposedOn is not null)
        {
            throw new DomainException("A disposed asset cannot change.");
        }
        if (cost <= 0 || residualValue < 0 || residualValue >= cost)
        {
            throw new DomainException("cost must be positive and residual_value at least 0 and below cost.");
        }
        if (usefulLifeMonths is < 1 or > 1200)
        {
            throw new DomainException("useful_life_months must be 1-1200.");
        }
        Code = Names.Require(code, "code");
        Name = Names.Require(name);
        AcquisitionDate = acquisitionDate;
        Cost = cost;
        ResidualValue = residualValue;
        UsefulLifeMonths = usefulLifeMonths;
        AssetAccountId = assetAccountId;
        AccumulatedDepreciationAccountId = accumulatedDepreciationAccountId;
        DepreciationExpenseAccountId = depreciationExpenseAccountId;
        if (!isNew)
        {
            Version++;
        }
    }

    /// <summary>
    /// Removes the asset from the books: Dr accumulated depreciation, Cr the asset at cost, and the net book value
    /// to the gain or loss account. Sale proceeds post separately (a receipt to the same gain or loss account).
    /// </summary>
    public JournalDraft DisposalJournal(DateOnly date, decimal accumulated, Guid gainOrLossAccountId) =>
        new(date,
        [
            new DraftLine(AccumulatedDepreciationAccountId, accumulated, $"Disposal of {Code} {Name}"),
            new DraftLine(gainOrLossAccountId, Cost - accumulated, $"Disposal of {Code} {Name} (net book value)"),
            new DraftLine(AssetAccountId, -Cost, $"Disposal of {Code} {Name}"),
        ], Code, $"Disposal of {Code} {Name}");

    public void Disposed(DateOnly on, Guid journalId)
    {
        DisposedOn = on;
        DisposalJournalId = journalId;
        Version++;
    }
}

/// <summary>A month's depreciation for every asset in the register, posted as one journal; one run per month.</summary>
public sealed class DepreciationRun
{
    public const string Kind = "depreciation_run";

    private readonly List<DepreciationLine> _lines = [];

    private DepreciationRun() { }

    public DepreciationRun(Guid id, Guid orgId, Guid ledgerId, DateOnly period, IEnumerable<DepreciationLine> lines, DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        Period = period;
        CreatedAt = createdAt;
        _lines.AddRange(lines.Select((l, i) => l.Numbered(id, i + 1)));
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }
    public Guid LedgerId { get; private set; }

    /// <summary>The first day of the month.</summary>
    public DateOnly Period { get; private set; }

    public Guid? JournalId { get; private set; }
    public DocumentStatus Status { get; private set; } = DocumentStatus.Draft;
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<DepreciationLine> Lines => _lines;

    public void PostedAs(Guid journalId)
    {
        JournalId = journalId;
        Status = DocumentStatus.Posted;
    }

    public void Delete() => Status = DocumentStatus.Deleted;

    /// <summary>Dr each depreciation expense account, Cr each accumulated depreciation account, summed by account; dated the month end.</summary>
    public JournalDraft ToJournal(IReadOnlyDictionary<Guid, FixedAsset> assets)
    {
        var text = $"Depreciation {Period:yyyy-MM}";
        var lines = _lines.GroupBy(l => assets[l.AssetId].DepreciationExpenseAccountId).Select(g => new DraftLine(g.Key, g.Sum(l => l.Amount), text))
            .Concat(_lines.GroupBy(l => assets[l.AssetId].AccumulatedDepreciationAccountId).Select(g => new DraftLine(g.Key, -g.Sum(l => l.Amount), text)))
            .ToList();
        return new JournalDraft(Period.AddMonths(1).AddDays(-1), lines, $"DEP-{Period:yyyy-MM}", text);
    }
}

public sealed class DepreciationLine
{
    private DepreciationLine() { }

    public DepreciationLine(Guid assetId, decimal amount)
    {
        AssetId = assetId;
        Amount = amount;
    }

    public Guid RunId { get; private set; }
    public int LineNo { get; private set; }
    public Guid AssetId { get; private set; }
    public decimal Amount { get; private set; }

    internal DepreciationLine Numbered(Guid runId, int lineNo)
    {
        RunId = runId;
        LineNo = lineNo;
        return this;
    }
}
