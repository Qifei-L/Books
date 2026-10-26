using Books.Api.Auth;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;

namespace Books.Api.Http;

public static class MembershipEndpoints
{
    public sealed record GrantRequest(Guid LedgerId, string Access, string? Role = null);

    public sealed record SetAccessRequest(string Role, IReadOnlyList<GrantRequest> Grants);

    public static void MapMembershipEndpoints(this RouteGroupBuilder api)
    {
        var members = api.MapGroup("/organizations/{orgId:guid}/memberships");

        members.MapGet("", async (Guid orgId, PrincipalAccessor principals, MembershipService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ct)));

        members.MapGet("/me", async (Guid orgId, PrincipalAccessor principals, MembershipService service, HttpContext http, CancellationToken ct) =>
        {
            var me = await service.GetMineAsync(await principals.GetAsync(ct), orgId, ct);
            return Envelope.Ok(http, me, me.Version);
        });

        members.MapPost("/me/leave", async (Guid orgId, PrincipalAccessor principals, MembershipService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.LeaveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ct)));

        members.MapPut("/{membershipId:guid}/access", async (Guid orgId, Guid membershipId, SetAccessRequest body, PrincipalAccessor principals, MembershipService service, HttpContext http, CancellationToken ct) =>
        {
            var grants = (body.Grants ?? throw BooksProblem.Invalid("grants is required; [] means no ledgers."))
                .Select(g => new GrantInput(g.LedgerId, ParseAccess(g.Access), g.Role)).ToList();
            return WriteHttp.Result(http, await service.SetAccessAsync(
                await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, membershipId, ParseRole(body.Role), grants, ct));
        });

        members.MapPost("/{membershipId:guid}/remove", async (Guid orgId, Guid membershipId, PrincipalAccessor principals, MembershipService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.RemoveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, membershipId, ct)));
    }

    internal static MembershipRole ParseRole(string? role) => role switch
    {
        "admin" => MembershipRole.Admin,
        "member" => MembershipRole.Member,
        _ => throw BooksProblem.Invalid("role must be admin or member."),
    };

    internal static LedgerAccess ParseAccess(string? access) => access switch
    {
        "read" => LedgerAccess.Read,
        "edit" => LedgerAccess.Edit,
        _ => throw BooksProblem.Invalid("access must be read or edit."),
    };
}
