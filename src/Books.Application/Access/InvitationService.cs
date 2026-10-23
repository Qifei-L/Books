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
            db.InvitationGrants.AddRange(command.Grants.Select(g => new InvitationGrant(invitation, g.LedgerId, g.Access)));
            scope.Audit("invitation.created", "invitation", invitation.Id, new
            {
                email = key,
                role = Text.Of(command.Role),
                grants = command.Grants.Select(g => new GrantDto(g.LedgerId, Text.Of(g.Access))),
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

    internal static BooksProblem Unavailable() =>
        new(410, "INVITE_UNAVAILABLE", "The invitation has expired, was revoked, or is no longer valid.");

    private Task<bool> IsActiveMemberAsync(Guid orgId, string emailKey, CancellationToken ct) =>
        db.Identities.Where(i => i.VerifiedEmail == emailKey)
            .Join(db.Memberships, i => i.UserId, m => m.UserId, (i, m) => m)
            .AnyAsync(m => m.OrgId == orgId && m.Status == MembershipStatus.Active, ct);

    private static bool SameGrants(IReadOnlyList<GrantInput> a, IReadOnlyList<GrantInput> b) =>
        a.OrderBy(g => g.LedgerId).SequenceEqual(b.OrderBy(g => g.LedgerId));

    internal async Task<IReadOnlyList<GrantInput>> GrantsOfAsync(Guid invitationId, CancellationToken ct) =>
        await db.InvitationGrants.AsNoTracking().Where(g => g.InvitationId == invitationId)
            .Select(g => new GrantInput(g.LedgerId, g.Access)).ToListAsync(ct);

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
            var grants = (await GrantsOfAsync(i.Id, ct)).Select(g => new GrantDto(g.LedgerId, Text.Of(g.Access))).ToList();
            result.Add(new InvitationDto(i.Id, i.OrgId, row.Name, i.TargetEmailKey, Text.Of(i.ProposedRole), grants,
                Text.Of(i.StatusAt(now)), i.ExpiresAt, i.CreatedAt, i.Version.ToString()));
        }
        return result;
    }
}
