using Books.Application.Common;
using Books.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Access;

public sealed record OrganizationDto(Guid Id, string Name, string State, string Role, string Version, DateTimeOffset CreatedAt);

public sealed record MembershipDto(Guid Id, Guid OrgId, Guid UserId, string Role, string Status, int Generation, string Version);

public sealed class OrganizationService(IBooksDb db, WriteRunner writes)
{
    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, string name, CancellationToken ct)
    {
        if (principal.VerifiedEmail is null)
        {
            throw BooksProblem.EmailNotVerified();
        }

        return writes.RunAsync(principal, new WriteRequest("organization.create", null, meta), scope =>
        {
            var organization = new Organization(Guid.CreateVersion7(), name, principal.UserId, scope.Now);
            var membership = new Membership(Guid.CreateVersion7(), organization.Id, principal.UserId, MembershipRole.Admin, scope.Now);
            db.Organizations.Add(organization);
            db.Memberships.Add(membership);
            scope.Audit("organization.created", "organization", organization.Id, new { name = organization.Name }, organization.Id);
            scope.Audit("membership.created", "membership", membership.Id, new { user_id = principal.UserId, role = "admin" }, organization.Id);

            return Task.FromResult(WriteResult.Of(201, new
            {
                organization = ToDto(organization, membership),
                membership = ToDto(membership),
            }, organization.Version));
        }, ct);
    }

    /// <summary>Organizations where the caller is currently an active member.</summary>
    public async Task<IReadOnlyList<OrganizationDto>> ListMineAsync(Principal principal, CancellationToken ct)
    {
        var rows = await db.Memberships.AsNoTracking()
            .Where(m => m.UserId == principal.UserId && m.Status == MembershipStatus.Active)
            .Join(db.Organizations, m => m.OrgId, o => o.Id, (m, o) => new { m, o })
            .Where(x => x.o.State == OrganizationState.Active)
            .OrderBy(x => x.o.CreatedAt).ThenBy(x => x.o.Id)
            .ToListAsync(ct);
        return rows.Select(x => ToDto(x.o, x.m)).ToList();
    }

    internal static OrganizationDto ToDto(Organization o, Membership m) =>
        new(o.Id, o.Name, Text.Of(o.State), Text.Of(m.Role), o.Version.ToString(), o.CreatedAt);

    internal static MembershipDto ToDto(Membership m) =>
        new(m.Id, m.OrgId, m.UserId, Text.Of(m.Role), Text.Of(m.Status), m.Generation, m.Version.ToString());
}
