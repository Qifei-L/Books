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
        var actor = organization is null
            ? null
            : await db.Memberships.AsNoTracking().SingleOrDefaultAsync(
                m => m.OrgId == orgId && m.UserId == principal.UserId && m.Status == MembershipStatus.Active, ct);
        var result = await read(new OrgScope(db, principal, organization, actor));
        await tx.CommitAsync(ct);
        return result;
    }
}
