using System.Data;
using Books.Application.Access;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Common;

/// <summary>The organization and the actor's current membership, as read inside the current transaction.</summary>
public class OrgScope(IBooksDb db, Principal principal, Organization? organization, Membership? actor)
{
    public IBooksDb Db => db;
    public Principal Principal => principal;

    public Organization Organization => organization ?? throw BooksProblem.NotFound();

    /// <summary>The actor's active membership, or 404 so other organizations' existence is never revealed.</summary>
    public Membership RequireMember()
    {
        if (organization is null || actor is null)
        {
            throw BooksProblem.NotFound();
        }
        if (organization.State != OrganizationState.Active)
        {
            throw new BooksProblem(403, "ORG_RECOVERY_REQUIRED", "This organization is locked for recovery.");
        }
        return actor;
    }

    public Membership RequireAdmin()
    {
        var member = RequireMember();
        return member.Role == MembershipRole.Admin ? member : throw BooksProblem.Forbidden();
    }

    /// <summary>
    /// Every ledger the actor can currently use, with the access level. Administrators get edit on all
    /// ledgers of the organization; members get only their current grants.
    /// </summary>
    public async Task<Dictionary<Guid, LedgerAccess>> VisibleLedgersAsync(CancellationToken ct)
    {
        var member = RequireMember();
        if (member.Role == MembershipRole.Admin)
        {
            return await db.Ledgers.AsNoTracking().Where(l => l.OrgId == member.OrgId)
                .ToDictionaryAsync(l => l.Id, _ => LedgerAccess.Edit, ct);
        }
        return await db.LedgerGrants.AsNoTracking()
            .Where(g => g.MembershipId == member.Id && g.MembershipGeneration == member.Generation)
            .ToDictionaryAsync(g => g.LedgerId, g => g.Access, ct);
    }

    /// <summary>The actor's current access to one ledger of this organization, or null when it has none.</summary>
    public async Task<LedgerAccess?> LedgerAccessAsync(Guid ledgerId, CancellationToken ct) =>
        (await VisibleLedgersAsync(ct)).TryGetValue(ledgerId, out var access) ? access : null;

    /// <summary>404 when the ledger is invisible to the actor; 403 when visible but the access is too low.</summary>
    public async Task<LedgerAccess> RequireLedgerAsync(Guid ledgerId, LedgerAccess needed, CancellationToken ct)
    {
        var access = await LedgerAccessAsync(ledgerId, ct) ?? throw BooksProblem.NotFound();
        return access >= needed ? access : throw BooksProblem.Forbidden();
    }

    /// <summary>The actor's role in one ledger; administrators act as managers. Null when the ledger is invisible.</summary>
    public async Task<LedgerRole?> LedgerRoleAsync(Guid ledgerId, CancellationToken ct)
    {
        var member = RequireMember();
        if (member.Role == MembershipRole.Admin)
        {
            return LedgerRole.Manager;
        }
        var grant = await db.LedgerGrants.AsNoTracking().SingleOrDefaultAsync(
            g => g.MembershipId == member.Id && g.MembershipGeneration == member.Generation && g.LedgerId == ledgerId, ct);
        return grant?.EffectiveRole;
    }

    /// <summary>Capabilities combine the organization role with the ledger role (viewer/bookkeeper/accountant/manager).</summary>
    public async Task<bool> CanAsync(Guid ledgerId, LedgerCapability capability, CancellationToken ct)
    {
        var role = await LedgerRoleAsync(ledgerId, ct);
        return role is { } found && LedgerRoles.Allows(found, capability);
    }

    /// <summary>404 for a ledger the actor cannot see, 403 when its ledger role does not carry the capability.</summary>
    public async Task RequireCapabilityAsync(Guid ledgerId, LedgerCapability capability, CancellationToken ct)
    {
        var role = await LedgerRoleAsync(ledgerId, ct) ?? throw BooksProblem.NotFound();
        if (!LedgerRoles.Allows(role, capability))
        {
            throw BooksProblem.Forbidden();
        }
    }
}

/// <summary>
/// Runs an organization read in a short transaction that holds the organization row FOR SHARE,
/// so access is checked against current membership and a concurrent revocation cannot interleave.
/// </summary>
public sealed class ReadRunner(IBooksDb db)
{
    public async Task<T> RunAsync<T>(Principal principal, Guid orgId, Func<OrgScope, Task<T>> read, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var organization = (await db.Organizations.FromSql($"SELECT * FROM organizations WHERE id = {orgId} FOR SHARE")
            .AsNoTracking().ToListAsync(ct)).SingleOrDefault();
        var result = await read(await ScopeAsync(principal, orgId, organization, ct));
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Runs a report in one REPEATABLE READ, read-only transaction, so totals and drilldown come from the same
    /// snapshot (rule 42). Access is checked against that same snapshot.
    /// </summary>
    public async Task<T> SnapshotAsync<T>(Principal principal, Guid orgId, Func<OrgScope, Task<T>> read, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var organization = await db.Organizations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orgId, ct);
        var result = await read(await ScopeAsync(principal, orgId, organization, ct));
        await tx.CommitAsync(ct);
        return result;
    }

    private async Task<OrgScope> ScopeAsync(Principal principal, Guid orgId, Organization? organization, CancellationToken ct)
    {
        var actor = organization is null
            ? null
            : await db.Memberships.AsNoTracking().SingleOrDefaultAsync(
                m => m.OrgId == orgId && m.UserId == principal.UserId && m.Status == MembershipStatus.Active, ct);
        return new OrgScope(db, principal, organization, actor);
    }
}
