using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record CompanyDto(Guid Id, Guid OrgId, string Name, Guid LedgerId, string Version, DateTimeOffset CreatedAt);

public sealed record LedgerDto(
    Guid Id, Guid OrgId, Guid CompanyId, string CompanyName, string BaseCurrency, int FyStartMonth,
    DateOnly? ClosedThrough, string EffectiveAccess, string Version, DateTimeOffset CreatedAt, string? TaxPack = null);

public sealed record CreateCompany(string Name, string? BaseCurrency, int? FyStartMonth);

/// <summary>Companies and their ledgers. A company is always created together with its only ledger.</summary>
public sealed class CompanyService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, CreateCompany command, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("company.create", orgId, meta), scope =>
        {
            scope.RequireAdmin();
            var company = new Company(Guid.CreateVersion7(), orgId, command.Name, scope.Now);
            var ledger = new Ledger(Guid.CreateVersion7(), company, command.BaseCurrency ?? Currencies.Default, command.FyStartMonth ?? 1, scope.Now);
            db.Companies.Add(company);
            db.Ledgers.Add(ledger);
            scope.Audit("company.created", "company", company.Id, new { name = company.Name, ledger_id = ledger.Id });
            scope.Audit("ledger.created", "ledger", ledger.Id, new { company_id = company.Id, base_currency = ledger.BaseCurrency, fy_start_month = ledger.FyStartMonth });
            return Task.FromResult(WriteResult.Of(201, new
            {
                company = ToDto(company, ledger),
                ledger = ToDto(ledger, company, LedgerAccess.Edit),
            }, company.Version));
        }, ct);

    public Task<WriteResult> RenameAsync(Principal principal, WriteMeta meta, Guid orgId, Guid companyId, string name, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("company.rename", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            var company = await db.Companies.SingleOrDefaultAsync(c => c.OrgId == orgId && c.Id == companyId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(company.Version);
            var before = company.Name;
            company.Rename(name);
            scope.Audit("company.renamed", "company", company.Id, new { before, after = company.Name });
            var ledger = await db.Ledgers.AsNoTracking().SingleAsync(l => l.CompanyId == company.Id, ct);
            return WriteResult.Of(200, ToDto(company, ledger), company.Version);
        }, ct);

    /// <summary>Administrators see every company; members see companies whose ledger they can access.</summary>
    public Task<IReadOnlyList<CompanyDto>> ListAsync(Principal principal, Guid orgId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<CompanyDto>>(principal, orgId, async scope =>
        {
            var visible = await scope.VisibleLedgersAsync(ct);
            return (await LoadAsync(orgId, visible.Keys, ct)).Select(x => ToDto(x.Company, x.Ledger)).ToList();
        }, ct);

    public Task<CompanyDto> GetAsync(Principal principal, Guid orgId, Guid companyId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            var visible = await scope.VisibleLedgersAsync(ct);
            var found = (await LoadAsync(orgId, visible.Keys, ct)).Where(x => x.Company.Id == companyId).ToList();
            return found is [var match] ? ToDto(match.Company, match.Ledger) : throw BooksProblem.NotFound();
        }, ct);

    public Task<IReadOnlyList<LedgerDto>> ListLedgersAsync(Principal principal, Guid orgId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<LedgerDto>>(principal, orgId, async scope =>
        {
            var visible = await scope.VisibleLedgersAsync(ct);
            return (await LoadAsync(orgId, visible.Keys, ct)).Select(x => ToDto(x.Ledger, x.Company, visible[x.Ledger.Id])).ToList();
        }, ct);

    public Task<LedgerDto> GetLedgerAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            var access = await scope.LedgerAccessAsync(ledgerId, ct) ?? throw BooksProblem.NotFound();
            var found = (await LoadAsync(orgId, [ledgerId], ct)).Single();
            return ToDto(found.Ledger, found.Company, access);
        }, ct);

    private async Task<List<(Company Company, Ledger Ledger)>> LoadAsync(Guid orgId, IEnumerable<Guid> ledgerIds, CancellationToken ct)
    {
        var ids = ledgerIds.ToList();
        var rows = await db.Ledgers.AsNoTracking()
            .Where(l => l.OrgId == orgId && ids.Contains(l.Id))
            .Join(db.Companies.AsNoTracking(), l => l.CompanyId, c => c.Id, (l, c) => new { l, c })
            .OrderBy(x => x.c.CreatedAt).ThenBy(x => x.c.Id)
            .ToListAsync(ct);
        return rows.Select(x => (x.c, x.l)).ToList();
    }

    private static CompanyDto ToDto(Company c, Ledger l) => new(c.Id, c.OrgId, c.Name, l.Id, c.Version.ToString(), c.CreatedAt);

    internal static LedgerDto ToDto(Ledger l, Company c, LedgerAccess access) =>
        new(l.Id, l.OrgId, l.CompanyId, c.Name, l.BaseCurrency, l.FyStartMonth, l.ClosedThrough, Text.Of(access), l.Version.ToString(), l.CreatedAt, l.TaxPack);
}
