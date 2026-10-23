using Books.Application.Common;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Access;

public sealed record GrantDto(Guid LedgerId, string Access);

public sealed record MemberDto(Guid Id, Guid UserId, string DisplayName, string? Email, string Role, string Status, int Generation, IReadOnlyList<GrantDto> Grants, string Version);

public sealed record GrantInput(Guid LedgerId, LedgerAccess Access);

/// <summary>Members, roles and ledger grants. Only administrators change them (rule 3).</summary>
public sealed class MembershipService(IBooksDb db, WriteRunner writes, ReadRunner reads)
{
    public Task<IReadOnlyList<MemberDto>> ListAsync(Principal principal, Guid orgId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            scope.RequireAdmin();
            return await LoadAsync(orgId, null, ct);
        }, ct);

    public Task<MemberDto> GetMineAsync(Principal principal, Guid orgId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope => (await LoadAsync(orgId, scope.RequireMember().Id, ct)).Single(), ct);

    /// <summary>
    /// Replaces a member's role and complete grant set. Administrators have no grants; a member's grants
    /// are exactly the given list ([] means no ledgers). Old grants never survive a role change.
    /// </summary>
    public Task<WriteResult> SetAccessAsync(
        Principal principal, WriteMeta meta, Guid orgId, Guid membershipId, MembershipRole role, IReadOnlyList<GrantInput> grants, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("membership.set_access", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            var target = await FindActiveAsync(orgId, membershipId, ct);
            scope.CheckVersion(target.Version);
            await ValidateGrantsAsync(db, orgId, role, grants, ct);
            if (target.IsActiveAdmin && role != MembershipRole.Admin)
            {
                await EnsureAnotherAdminAsync(orgId, target.Id, ct);
            }

            var before = new { role = Text.Of(target.Role), grants = await GrantsOfAsync(target, ct) };
            var invalidated = target.IsActiveAdmin && role != MembershipRole.Admin
                ? await InvalidateInvitationsAsync(target, addressedToTarget: false, ct)
                : [];
            await db.LedgerGrants.Where(g => g.MembershipId == target.Id).ExecuteDeleteAsync(ct);
            target.ChangeRole(role);
            foreach (var grant in grants)
            {
                db.LedgerGrants.Add(new LedgerGrant(target, grant.LedgerId, grant.Access, principal.UserId, scope.Now));
            }
            var after = new { role = Text.Of(role), grants = grants.Select(g => new GrantDto(g.LedgerId, Text.Of(g.Access))).ToList() };
            scope.Audit("membership.access_changed", "membership", target.Id, new { before, after, invalidated_invitations = invalidated });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, (await LoadAsync(orgId, target.Id, ct)).Single(), target.Version);
        }, ct);

    public Task<WriteResult> RemoveAsync(Principal principal, WriteMeta meta, Guid orgId, Guid membershipId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("membership.remove", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            return await RemoveTargetAsync(scope, await FindActiveAsync(orgId, membershipId, ct), "membership.removed", ct);
        }, ct);

    /// <summary>The caller leaves the organization. A replay returns only the receipt, even after access is gone.</summary>
    public Task<WriteResult> LeaveAsync(Principal principal, WriteMeta meta, Guid orgId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("membership.leave", orgId, meta, ReplayRequiresMembership: false), async scope =>
            await RemoveTargetAsync(scope, scope.RequireMember(), "membership.left", ct), ct);

    private async Task<WriteResult> RemoveTargetAsync(WriteScope scope, Membership target, string action, CancellationToken ct)
    {
        scope.CheckVersion(target.Version);
        if (target.IsActiveAdmin)
        {
            await EnsureAnotherAdminAsync(target.OrgId, target.Id, ct);
        }
        var invalidated = await InvalidateInvitationsAsync(target, addressedToTarget: true, ct);
        await db.LedgerGrants.Where(g => g.MembershipId == target.Id).ExecuteDeleteAsync(ct);
        target.Remove(scope.Now);
        scope.Audit(action, "membership", target.Id, new { user_id = target.UserId, role = Text.Of(target.Role), invalidated_invitations = invalidated });
        return WriteResult.Of(200, new { id = target.Id, status = Text.Of(target.Status), removed_at = target.RemovedAt }, target.Version);
    }

    /// <summary>
    /// Rule 6: pending invitations sent by someone who stops being an administrator become invalid for good.
    /// Removing a member also invalidates pending invitations addressed to them, so a new one must be issued.
    /// </summary>
    private async Task<List<Guid>> InvalidateInvitationsAsync(Membership target, bool addressedToTarget, CancellationToken ct)
    {
        var emails = addressedToTarget
            ? await db.Identities.Where(i => i.UserId == target.UserId && i.VerifiedEmail != null).Select(i => i.VerifiedEmail!).ToListAsync(ct)
            : [];
        var invitations = await db.Invitations
            .Where(i => i.OrgId == target.OrgId && i.Status == InvitationStatus.Pending
                && ((target.Role == MembershipRole.Admin && i.InviterMembershipId == target.Id) || emails.Contains(i.TargetEmailKey)))
            .ToListAsync(ct);
        invitations.ForEach(i => i.Invalidate());
        return invitations.Select(i => i.Id).ToList();
    }

    /// <summary>Administrators hold no grants; a member's grants name distinct ledgers of this organization.</summary>
    internal static async Task ValidateGrantsAsync(IBooksDb db, Guid orgId, MembershipRole role, IReadOnlyList<GrantInput> grants, CancellationToken ct)
    {
        if (role == MembershipRole.Admin && grants.Count > 0)
        {
            throw BooksProblem.Invalid("Administrators have access to every ledger; grants must be empty.");
        }
        if (grants.Select(g => g.LedgerId).Distinct().Count() != grants.Count)
        {
            throw BooksProblem.Invalid("Each ledger may appear only once in grants.");
        }
        var ledgerIds = grants.Select(g => g.LedgerId).ToList();
        if (await db.Ledgers.CountAsync(l => l.OrgId == orgId && ledgerIds.Contains(l.Id), ct) != ledgerIds.Count)
        {
            throw BooksProblem.Invalid("Every granted ledger must belong to this organization.");
        }
    }

    /// <summary>Rule 8: counted under the organization lock, so two concurrent changes cannot both pass.</summary>
    private async Task EnsureAnotherAdminAsync(Guid orgId, Guid changing, CancellationToken ct)
    {
        var others = await db.Memberships.CountAsync(
            m => m.OrgId == orgId && m.Id != changing && m.Role == MembershipRole.Admin && m.Status == MembershipStatus.Active, ct);
        if (others == 0)
        {
            throw BooksProblem.Conflict("LAST_ADMIN", "Promote another administrator first; an organization must keep one.");
        }
    }

    private async Task<Membership> FindActiveAsync(Guid orgId, Guid membershipId, CancellationToken ct) =>
        await db.Memberships.SingleOrDefaultAsync(m => m.OrgId == orgId && m.Id == membershipId && m.Status == MembershipStatus.Active, ct)
        ?? throw BooksProblem.NotFound();

    private async Task<List<GrantDto>> GrantsOfAsync(Membership m, CancellationToken ct) =>
        await db.LedgerGrants.AsNoTracking()
            .Where(g => g.MembershipId == m.Id && g.MembershipGeneration == m.Generation)
            .OrderBy(g => g.LedgerId)
            .Select(g => new GrantDto(g.LedgerId, g.Access == LedgerAccess.Edit ? "edit" : "read"))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<MemberDto>> LoadAsync(Guid orgId, Guid? membershipId, CancellationToken ct)
    {
        var rows = await db.Memberships.AsNoTracking()
            .Where(m => m.OrgId == orgId && m.Status == MembershipStatus.Active && (membershipId == null || m.Id == membershipId))
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new
            {
                m,
                u.DisplayName,
                Email = db.Identities.Where(i => i.UserId == u.Id && i.VerifiedEmail != null).Select(i => i.VerifiedEmail).FirstOrDefault(),
            })
            .OrderBy(x => x.m.JoinedAt).ThenBy(x => x.m.Id)
            .ToListAsync(ct);
        var result = new List<MemberDto>();
        foreach (var row in rows)
        {
            var m = row.m;
            result.Add(new MemberDto(m.Id, m.UserId, row.DisplayName, row.Email, Text.Of(m.Role), Text.Of(m.Status), m.Generation, await GrantsOfAsync(m, ct), m.Version.ToString()));
        }
        return result;
    }
}
