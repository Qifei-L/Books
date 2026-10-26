using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reporting;

public sealed record ReportTemplateDto(Guid? Id, Guid OrgId, Guid? LedgerId, string Kind, string Name, string Status, long Version,
    string? EffectiveFrom, ReportTemplateSpec Definition, string? PublishedAt, bool IsDefault);

public sealed record EffectiveReportTemplateDto(string Kind, string Name, Guid? TemplateId, long Version, string? EffectiveFrom, string Source,
    ReportTemplateSpec Definition);

public sealed record ReportTemplatesDto(IReadOnlyList<ReportTemplateDto> Templates, IReadOnlyList<EffectiveReportTemplateDto> Effective,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Sections);

/// <summary>The template chosen for a period: its id (null for the code default), version and definition.</summary>
public sealed record ResolvedReportTemplate(Guid? Id, long Version, string Name, ReportTemplateSpec Spec);

public sealed record ReportTemplateInput(string Kind, string Name, ReportTemplateSpec Definition, DateOnly? EffectiveFrom);

/// <summary>
/// Versioned report templates. The code provides a default for every kind; a ledger may publish overrides that take
/// effect from a date, and a published version never changes. Client packs and tax workpapers read their section
/// order and on/off switches from the template effective for the period.
/// </summary>
public sealed class ReportTemplateService(IBooksDb db, ReadRunner reads, WriteRunner writes)
{
    public Task<ReportTemplatesDto> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var rows = await db.ReportTemplates.AsNoTracking()
                .Where(t => t.OrgId == orgId && (t.LedgerId == null || t.LedgerId == ledgerId))
                .OrderBy(t => t.Kind).ThenBy(t => t.Version)
                .ToListAsync(ct);
            var effective = new List<EffectiveReportTemplateDto>();
            foreach (var kind in Enum.GetValues<ReportTemplateKind>())
            {
                var spec = await ResolveAsync(orgId, ledgerId, kind, DateOnly.MaxValue, ct);
                var row = rows.Where(t => t.Kind == kind && t.Status == ReportTemplateStatus.Published)
                    .OrderByDescending(t => t.EffectiveFrom ?? DateOnly.MinValue)
                    .ThenByDescending(t => t.Version)
                    .FirstOrDefault(t => t.LedgerId == ledgerId);
                var source = row is not null ? "ledger" : rows.Any(t => t.Kind == kind && t.LedgerId == null && t.Status == ReportTemplateStatus.Published) ? "organization" : "default";
                effective.Add(new EffectiveReportTemplateDto(ReportTemplates.KindName(kind), row?.Name ?? ReportTemplates.DefaultNames[kind], row?.Id,
                    row?.Version ?? 0, row?.EffectiveFrom?.ToString("yyyy-MM-dd"), source, spec));
            }
            return new ReportTemplatesDto(rows.Select(ToDto).ToList(), effective,
                ReportTemplates.Sections.ToDictionary(x => ReportTemplates.KindName(x.Key), x => (IReadOnlyList<string>)x.Value));
        }, ct);

    public Task<ReportTemplateDto> GetAsync(Principal principal, Guid orgId, Guid ledgerId, Guid id, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            var row = await db.ReportTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id && t.OrgId == orgId
                && (t.LedgerId == null || t.LedgerId == ledgerId), ct) ?? throw BooksProblem.NotFound();
            return ToDto(row);
        }, ct);

    public Task<WriteResult> CreateDraftAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, ReportTemplateInput input, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("report_template.create", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var kind = ParseKind(input.Kind);
            var spec = Validate(kind, input.Definition);
            var version = (await db.ReportTemplates.Where(t => t.OrgId == orgId && t.LedgerId == ledgerId && t.Kind == kind)
                .Select(t => (long?)t.Version).MaxAsync(ct) ?? 0) + 1;
            var template = new ReportTemplate(Guid.CreateVersion7(), orgId, ledgerId, kind, input.Name, ReportTemplates.Serialize(spec),
                input.EffectiveFrom, principal.UserId, scope.Now, version);
            db.ReportTemplates.Add(template);
            scope.Audit("report_template.drafted", "report_template", template.Id, ToDto(template), orgId);
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, ToDto(template), template.Version);
        }, ct);

    public Task<WriteResult> UpdateDraftAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid id, ReportTemplateInput input,
        CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("report_template.update", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var template = await db.ReportTemplates.SingleOrDefaultAsync(t => t.Id == id && t.OrgId == orgId && t.LedgerId == ledgerId, ct)
                ?? throw BooksProblem.NotFound();
            if (template.Status != ReportTemplateStatus.Draft)
            {
                throw BooksProblem.Conflict("TEMPLATE_PUBLISHED", "A published template cannot change; draft a new version instead.");
            }
            scope.CheckVersion(template.Version);
            var kind = ParseKind(input.Kind);
            if (kind != template.Kind)
            {
                throw BooksProblem.Invalid("A template's kind cannot change.");
            }
            var spec = Validate(kind, input.Definition);
            try
            {
                template.Update(input.Name, ReportTemplates.Serialize(spec), input.EffectiveFrom, scope.Now);
            }
            catch (DomainException e)
            {
                throw BooksProblem.Invalid(e.Message);
            }
            scope.Audit("report_template.updated", "report_template", template.Id, ToDto(template), orgId);
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, ToDto(template), template.Version);
        }, ct);

    public Task<WriteResult> PublishAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid id, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("report_template.publish", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            scope.RequireAdmin();
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var template = await db.ReportTemplates.SingleOrDefaultAsync(t => t.Id == id && t.OrgId == orgId && t.LedgerId == ledgerId, ct)
                ?? throw BooksProblem.NotFound();
            if (template.Status == ReportTemplateStatus.Published)
            {
                return WriteResult.Of(200, ToDto(template), template.Version);
            }
            var clash = await db.ReportTemplates.AnyAsync(t => t.Id != template.Id && t.OrgId == orgId && t.LedgerId == ledgerId
                && t.Kind == template.Kind && t.Status == ReportTemplateStatus.Published && t.EffectiveFrom == template.EffectiveFrom, ct);
            if (clash)
            {
                throw BooksProblem.Conflict("TEMPLATE_EFFECTIVE_CLASH",
                    $"Another published {ReportTemplates.KindName(template.Kind)} template already applies from {template.EffectiveFrom?.ToString("yyyy-MM-dd") ?? "the earliest period"}.");
            }
            template.Publish(principal.UserId, scope.Now);
            scope.Audit("report_template.published", "report_template", template.Id, ToDto(template), orgId);
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, ToDto(template), template.Version);
        }, ct);

    /// <summary>
    /// The template effective for one kind at a date: a published ledger override wins over a published organization
    /// template, which wins over the code default. A version with no effective date is the earliest fallback.
    /// </summary>
    public async Task<ReportTemplateSpec> ResolveAsync(Guid orgId, Guid ledgerId, ReportTemplateKind kind, DateOnly asOf, CancellationToken ct) =>
        (await ResolveDetailedAsync(orgId, ledgerId, kind, asOf, ct)).Spec;

    /// <summary>As <see cref="ResolveAsync"/>, but also returns the published row's id so a report run can record it.</summary>
    public async Task<ResolvedReportTemplate> ResolveDetailedAsync(Guid orgId, Guid ledgerId, ReportTemplateKind kind, DateOnly asOf, CancellationToken ct)
    {
        var rows = await db.ReportTemplates.AsNoTracking()
            .Where(t => t.OrgId == orgId && t.Status == ReportTemplateStatus.Published && t.Kind == kind
                && (t.LedgerId == null || t.LedgerId == ledgerId)
                && (t.EffectiveFrom == null || t.EffectiveFrom <= asOf))
            .OrderByDescending(t => t.EffectiveFrom ?? DateOnly.MinValue).ThenByDescending(t => t.Version)
            .ToListAsync(ct);
        var row = rows.FirstOrDefault(t => t.LedgerId == ledgerId) ?? rows.FirstOrDefault(t => t.LedgerId == null);
        return row is null
            ? new ResolvedReportTemplate(null, 0, ReportTemplates.DefaultNames[kind], ReportTemplates.Default(kind))
            : new ResolvedReportTemplate(row.Id, row.Version, row.Name, ParseDefinition(row.Definition));
    }

    public static ReportTemplateSpec ParseDefinition(string definition) => ReportTemplates.Parse(definition);

    private static ReportTemplateSpec Validate(ReportTemplateKind kind, ReportTemplateSpec? definition)
    {
        if (definition is null)
        {
            throw BooksProblem.Invalid("definition is required.");
        }
        try
        {
            return ReportTemplates.Validate(kind, ReportTemplates.Serialize(definition));
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }

    private static ReportTemplateKind ParseKind(string value) =>
        ReportTemplates.TryKind(value, out var kind)
            ? kind
            : throw BooksProblem.Invalid($"kind must be one of {string.Join(", ", Enum.GetValues<ReportTemplateKind>().Select(ReportTemplates.KindName))}.");

    private static ReportTemplateDto ToDto(ReportTemplate t) => new(t.Id, t.OrgId, t.LedgerId, ReportTemplates.KindName(t.Kind), t.Name,
        t.Status == ReportTemplateStatus.Draft ? "draft" : "published", t.Version, t.EffectiveFrom?.ToString("yyyy-MM-dd"),
        ParseDefinition(t.Definition), t.PublishedAt?.ToString("O"), false);
}
