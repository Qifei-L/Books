using Books.Application.Common;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Access;

public sealed record InvitationDto(
    Guid Id, Guid OrgId, string OrganizationName, string Email, string Role, IReadOnlyList<GrantDto> Grants,
    string Status, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, string Version);

public sealed record CreateInvitation(string Email, MembershipRole Role, IReadOnlyList<GrantInput> Grants);

/// <summary>Invitations to one verified email (rule 6). Only administrators invite and revoke.</summary>
public sealed class InvitationService(IBooksDb db, WriteRunner writes, ReadRunner reads, TimeProvider clock)
{
    /// <summary>
    /// An identical pending invitation is returned unchanged (expiry is not extended); a different one for the
    /// same person conflicts and must be revoked first. Active members cannot be invited.
    /// </summary>
    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, CreateInvitation command, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("invitation.create", orgId, meta), async scope =>
        {
            var inviter = scope.RequireAdmin();
            var email = command.Email?.Trim() ?? "";
            if (email.Length is < 3 or > 320 || !email.Contains('@'))
            {
                throw BooksProblem.Invalid("email must be an email address.");
            }
            var key = EmailKey.Normalize(email);
            await MembershipService.ValidateGrantsAsync(db, orgId, command.Role, command.Grants, ct);
            if (await IsActiveMemberAsync(orgId, key, ct))
            {
                throw BooksProblem.Conflict("ALREADY_MEMBER", "This person is already a member; change their access instead.");
            }

            var pending = await db.Invitations.SingleOrDefaultAsync(i => i.OrgId == orgId && i.TargetEmailKey == key && i.Status == InvitationStatus.Pending, ct);
            if (pending is not null && pending.StatusAt(scope.Now) == InvitationStatus.Expired)
            {
                pending.MarkExpired();
                await db.SaveChangesAsync(ct);
                pending = null;
            }
            if (pending is not null)
            {
                var same = pending.ProposedRole == command.Role && SameGrants(await GrantsOfAsync(pending.Id, ct), command.Grants);
                return same
                    ? WriteResult.Of(200, (await LoadAsync(i => i.Id == pending.Id, ct)).Single(), pending.Version)
                    : throw BooksProblem.Conflict("INVITE_CONFLICT", "A different invitation is pending for this person; revoke it first.");
            }

            var invitation = new Invitation(Guid.CreateVersion7(), inviter, key, command.Role, scope.Now);
            db.Invitations.Add(invitation);
            db.InvitationGrants.AddRange(command.Grants.Select(g => new InvitationGrant(invitation, g.LedgerId, g.Access, MembershipService.ParsedRole(g))));
            scope.Audit("invitation.created", "invitation", invitation.Id, new
            {
                email = key,
                role = Text.Of(command.Role),
                grants = command.Grants.Select(g => new GrantDto(g.LedgerId, Text.Of(g.Access), MembershipService.RoleName(g))),
                expires_at = invitation.ExpiresAt,
            });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(201, (await LoadAsync(i => i.Id == invitation.Id, ct)).Single(), invitation.Version);
        }, ct);

    public Task<IReadOnlyList<InvitationDto>> ListAsync(Principal principal, Guid orgId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            scope.RequireAdmin();
            return await LoadAsync(i => i.OrgId == orgId, ct);
        }, ct);

    public Task<WriteResult> RevokeAsync(Principal principal, WriteMeta meta, Guid orgId, Guid invitationId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("invitation.revoke", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.OrgId == orgId && i.Id == invitationId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(invitation.Version);
            switch (invitation.StatusAt(scope.Now))
            {
                case InvitationStatus.Accepted:
                    throw BooksProblem.Conflict("INVITE_ALREADY_ACCEPTED", "The invitation was already accepted; remove the member instead.");
                case not InvitationStatus.Pending:
                    throw Unavailable();
            }
            invitation.Revoke();
            scope.Audit("invitation.revoked", "invitation", invitation.Id, new { email = invitation.TargetEmailKey });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(200, (await LoadAsync(i => i.Id == invitation.Id, ct)).Single(), invitation.Version);
        }, ct);

    /// <summary>Pending, unexpired invitations addressed to the caller's current verified email.</summary>
    public async Task<IReadOnlyList<InvitationDto>> InboxAsync(Principal principal, CancellationToken ct)
    {
        if (principal.VerifiedEmail is not { } email)
        {
            return [];
        }
        var now = clock.GetUtcNow();
        return await LoadAsync(i => i.TargetEmailKey == email && i.Status == InvitationStatus.Pending && i.ExpiresAt > now, ct);
    }

    /// <summary>
    /// The recipient explicitly accepts. Under the organization lock: the invitation is still pending and unexpired,
    /// the caller's current verified email matches, and the inviter is still an administrator. Then the membership
    /// is created (or a removed one rejoins with a new generation), only the offered grants are written, and the
    /// invitation is consumed.
    /// </summary>
    public async Task<WriteResult> AcceptAsync(Principal principal, WriteMeta meta, Guid invitationId, CancellationToken ct)
    {
        var email = principal.VerifiedEmail ?? throw BooksProblem.EmailNotVerified();
        var orgId = await db.Invitations.AsNoTracking()
            .Where(i => i.Id == invitationId && i.TargetEmailKey == email)
            .Select(i => (Guid?)i.OrgId).SingleOrDefaultAsync(ct) ?? throw BooksProblem.NotFound();

        return await writes.RunAsync(principal, new WriteRequest("invitation.accept", orgId, meta), async scope =>
        {
            var invitation = await db.Invitations.SingleAsync(i => i.Id == invitationId, ct);
            scope.CheckVersion(invitation.Version);
            if (invitation.StatusAt(scope.Now) != InvitationStatus.Pending || scope.Organization.State != OrganizationState.Active)
            {
                throw Unavailable();
            }
            var inviter = await db.Memberships.SingleAsync(m => m.Id == invitation.InviterMembershipId, ct);
            if (!inviter.IsActiveAdmin)
            {
                throw Unavailable();
            }

            var membership = await db.Memberships.SingleOrDefaultAsync(m => m.OrgId == orgId && m.UserId == principal.UserId, ct);
            if (membership?.Status == MembershipStatus.Active)
            {
                throw BooksProblem.Conflict("ALREADY_MEMBER", "You are already a member of this organization.");
            }
            if (membership is null)
            {
                membership = new Membership(Guid.CreateVersion7(), orgId, principal.UserId, invitation.ProposedRole, scope.Now);
                db.Memberships.Add(membership);
            }
            else
            {
                membership.Rejoin(invitation.ProposedRole, scope.Now);
            }

            var grants = await GrantsOfAsync(invitation.Id, ct);
            db.LedgerGrants.AddRange(grants.Select(g => new LedgerGrant(membership, g.LedgerId, g.Access, inviter.UserId, scope.Now, MembershipService.ParsedRole(g))));
            invitation.Accept(principal.UserId, scope.Now);
            scope.Audit("invitation.accepted", "invitation", invitation.Id, new { membership_id = membership.Id });
            scope.Audit("membership.joined", "membership", membership.Id, new
            {
                user_id = principal.UserId,
                role = Text.Of(membership.Role),
                generation = membership.Generation,
                grants = grants.Select(g => new GrantDto(g.LedgerId, Text.Of(g.Access), MembershipService.RoleName(g))),
            });

            var displayName = await db.Users.Where(u => u.Id == principal.UserId).Select(u => u.DisplayName).SingleAsync(ct);
            return WriteResult.Of(200, new
            {
                membership = new MemberDto(membership.Id, principal.UserId, displayName, principal.VerifiedEmail, Text.Of(membership.Role), Text.Of(membership.Status),
                    membership.Generation, grants.Select(g => new GrantDto(g.LedgerId, Text.Of(g.Access), MembershipService.RoleName(g))).ToList(), membership.Version.ToString()),
                organization_id = orgId,
            }, membership.Version);
        }, ct);
    }

    internal static BooksProblem Unavailable() =>
        new(410, "INVITE_UNAVAILABLE", "The invitation has expired, was revoked, or is no longer valid.");

    private Task<bool> IsActiveMemberAsync(Guid orgId, string emailKey, CancellationToken ct) =>
        db.Identities.Where(i => i.VerifiedEmail == emailKey)
            .Join(db.Memberships, i => i.UserId, m => m.UserId, (i, m) => m)
            .AnyAsync(m => m.OrgId == orgId && m.Status == MembershipStatus.Active, ct);

    private static bool SameGrants(IReadOnlyList<GrantInput> a, IReadOnlyList<GrantInput> b) =>
        a.OrderBy(g => g.LedgerId).SequenceEqual(b.OrderBy(g => g.LedgerId));

    internal async Task<IReadOnlyList<GrantInput>> GrantsOfAsync(Guid invitationId, CancellationToken ct)
    {
        var rows = await db.InvitationGrants.AsNoTracking().Where(g => g.InvitationId == invitationId)
            .OrderBy(g => g.LedgerId).Select(g => new { g.LedgerId, g.Access, g.Role }).ToListAsync(ct);
        return rows.Select(g => new GrantInput(g.LedgerId, g.Access, g.Role is { } role ? LedgerRoles.Name(role) : null)).ToList();
    }

    private async Task<IReadOnlyList<InvitationDto>> LoadAsync(System.Linq.Expressions.Expression<Func<Invitation, bool>> filter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await db.Invitations.AsNoTracking().Where(filter)
            .Join(db.Organizations, i => i.OrgId, o => o.Id, (i, o) => new { i, o.Name })
            .OrderBy(x => x.i.CreatedAt).ThenBy(x => x.i.Id)
            .ToListAsync(ct);
        var result = new List<InvitationDto>();
        foreach (var row in rows)
        {
            var i = row.i;
            var grants = (await GrantsOfAsync(i.Id, ct)).Select(g => new GrantDto(g.LedgerId, Text.Of(g.Access), MembershipService.RoleName(g))).ToList();
            result.Add(new InvitationDto(i.Id, i.OrgId, row.Name, i.TargetEmailKey, Text.Of(i.ProposedRole), grants,
                Text.Of(i.StatusAt(now)), i.ExpiresAt, i.CreatedAt, i.Version.ToString()));
        }
        return result;
    }
}
