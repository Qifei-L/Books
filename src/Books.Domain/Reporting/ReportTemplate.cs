using System.Text.Json;
using System.Text.Json.Serialization;
using Books.Domain.Access;

namespace Books.Domain.Reporting;

/// <summary>What a report template lays out. One row per draft or published version.</summary>
public enum ReportTemplateKind
{
    ClientPack,
    FinancialStatements,
    TrialBalance,
    GlDetail,
    Aging,
    TaxReturn,
    TaxWorkpaper,
}

public enum ReportTemplateStatus { Draft, Published }

/// <summary>
/// A section of a template, in order. Disabled sections stay in the definition so turning one back on does not
/// lose its place; the renderer only draws enabled ones.
/// </summary>
public sealed record ReportTemplateSection(string Key, bool Enabled = true, string? Label = null);

/// <summary>The template's JSON. Section keys are a fixed vocabulary per kind, checked before a row is saved.</summary>
public sealed record ReportTemplateSpec(string Title, IReadOnlyList<ReportTemplateSection> Sections)
{
    [JsonIgnore]
    public IReadOnlyList<string> EnabledSections => Sections.Where(s => s.Enabled).Select(s => s.Key).ToList();
}

/// <summary>
/// A ledger's (or, for organization defaults, the organization's) report template. Drafts change; published versions
/// never do, and a period keeps the version that was effective when it was filed (owner decision).
/// </summary>
public sealed class ReportTemplate
{
    private ReportTemplate() { }

    public ReportTemplate(Guid id, Guid orgId, Guid? ledgerId, ReportTemplateKind kind, string name, string definition,
        DateOnly? effectiveFrom, Guid createdBy, DateTimeOffset createdAt, long version = 1)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("name is required.");
        }
        if (version < 1)
        {
            throw new DomainException("version must be positive.");
        }
        Id = id;
        OrgId = orgId;
        LedgerId = ledgerId;
        Kind = kind;
        Name = name.Trim();
        Definition = definition;
        EffectiveFrom = effectiveFrom;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        Version = version;
    }

    public Guid Id { get; private set; }
    public Guid OrgId { get; private set; }

    /// <summary>Null for an organization-wide default; set for a ledger override.</summary>
    public Guid? LedgerId { get; private set; }

    public ReportTemplateKind Kind { get; private set; }
    public string Name { get; private set; } = "";

    /// <summary>One version number per (organization, ledger, kind), assigned by the service.</summary>
    public long Version { get; private set; } = 1;

    public ReportTemplateStatus Status { get; private set; } = ReportTemplateStatus.Draft;

    /// <summary>The first period the version applies to; null means the earliest published version is the fallback.</summary>
    public DateOnly? EffectiveFrom { get; private set; }

    /// <summary>The ReportTemplateSpec JSON.</summary>
    public string Definition { get; private set; } = "";

    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public void Update(string name, string definition, DateOnly? effectiveFrom, DateTimeOffset now)
    {
        if (Status != ReportTemplateStatus.Draft)
        {
            throw new DomainException("A published template cannot change; draft a new version instead.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("name is required.");
        }
        Name = name.Trim();
        Definition = definition;
        EffectiveFrom = effectiveFrom;
        UpdatedAt = now;
        Version++;
    }

    public void Publish(Guid by, DateTimeOffset at)
    {
        if (Status != ReportTemplateStatus.Published)
        {
            Status = ReportTemplateStatus.Published;
            PublishedBy = by;
            PublishedAt = at;
            UpdatedAt = at;
        }
    }
}

/// <summary>The code-provided defaults and the vocabulary every stored definition is checked against.</summary>
public static class ReportTemplates
{
    public static readonly IReadOnlyDictionary<ReportTemplateKind, string[]> Sections = new Dictionary<ReportTemplateKind, string[]>
    {
        [ReportTemplateKind.ClientPack] = ["cover", "pl", "bs", "tb", "aging", "signatures"],
        [ReportTemplateKind.FinancialStatements] = ["pl", "bs"],
        [ReportTemplateKind.TrialBalance] = ["tb"],
        [ReportTemplateKind.GlDetail] = ["gl"],
        [ReportTemplateKind.Aging] = ["ar", "ap"],
        [ReportTemplateKind.TaxReturn] = ["boxes"],
        [ReportTemplateKind.TaxWorkpaper] = ["boxes", "reconciliation", "state_charge", "non_deductible", "issues", "signatures"],
    };

    public static readonly IReadOnlyDictionary<ReportTemplateKind, string> DefaultNames = new Dictionary<ReportTemplateKind, string>
    {
        [ReportTemplateKind.ClientPack] = "Client report pack",
        [ReportTemplateKind.FinancialStatements] = "Financial statements",
        [ReportTemplateKind.TrialBalance] = "Trial balance",
        [ReportTemplateKind.GlDetail] = "GL detail",
        [ReportTemplateKind.Aging] = "AR/AP aging",
        [ReportTemplateKind.TaxReturn] = "Tax return workpaper",
        [ReportTemplateKind.TaxWorkpaper] = "Tax workpaper",
    };

    public static ReportTemplateSpec Default(ReportTemplateKind kind) =>
        new(DefaultNames[kind], Sections[kind].Select(key => new ReportTemplateSection(key)).ToList());

    public static IReadOnlyList<string> Enabled(ReportTemplateSpec spec) => spec.EnabledSections;

    /// <summary>Parses and checks a definition; every problem is reported at once.</summary>
    public static ReportTemplateSpec Validate(ReportTemplateKind kind, string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            throw new DomainException("definition is required.");
        }
        ReportTemplateSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<ReportTemplateSpec>(definition, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new DomainException($"definition is not valid JSON: {e.Message}");
        }
        if (spec is null)
        {
            throw new DomainException("definition is required.");
        }
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(spec.Title))
        {
            errors.Add("title is required.");
        }
        var allowed = Sections[kind];
        var seen = new HashSet<string>();
        foreach (var section in spec.Sections ?? [])
        {
            if (!allowed.Contains(section.Key))
            {
                errors.Add($"{section.Key} is not a section of {KindName(kind)}; use {string.Join(", ", allowed)}.");
            }
            else if (!seen.Add(section.Key))
            {
                errors.Add($"{section.Key} appears more than once.");
            }
        }
        if (spec.Sections is not { Count: > 0 })
        {
            errors.Add("at least one section is required.");
        }
        return errors.Count == 0
            ? new ReportTemplateSpec(spec.Title!.Trim(), spec.Sections!.Select(s => new ReportTemplateSection(s.Key, s.Enabled, s.Label)).ToList())
            : throw new DomainException(string.Join(" ", errors));
    }

    public static string Serialize(ReportTemplateSpec spec) => JsonSerializer.Serialize(spec, JsonOptions);

    /// <summary>Reads a stored definition without re-checking its kind (the row already knows it).</summary>
    public static ReportTemplateSpec Parse(string definition) =>
        JsonSerializer.Deserialize<ReportTemplateSpec>(definition, JsonOptions)
        ?? throw new DomainException("definition is required.");

    public static string KindName(ReportTemplateKind kind) => kind switch
    {
        ReportTemplateKind.ClientPack => "client_pack",
        ReportTemplateKind.FinancialStatements => "financial_statements",
        ReportTemplateKind.TrialBalance => "trial_balance",
        ReportTemplateKind.GlDetail => "gl_detail",
        ReportTemplateKind.Aging => "aging",
        ReportTemplateKind.TaxReturn => "tax_return",
        _ => "tax_workpaper",
    };

    public static bool TryKind(string? value, out ReportTemplateKind kind)
    {
        kind = default;
        foreach (var candidate in Enum.GetValues<ReportTemplateKind>())
        {
            if (KindName(candidate) == value)
            {
                kind = candidate;
                return true;
            }
        }
        return false;
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };
}
