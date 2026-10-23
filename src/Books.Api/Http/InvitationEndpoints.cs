using Books.Api.Auth;
using Books.Application.Access;
using Books.Application.Common;

namespace Books.Api.Http;

public static class InvitationEndpoints
{
    public sealed record CreateInvitationRequest(string Email, string Role, IReadOnlyList<MembershipEndpoints.GrantRequest>? Grants);

    public static void MapInvitationEndpoints(this RouteGroupBuilder api)
    {
        var invitations = api.MapGroup("/organizations/{orgId:guid}/invitations");

        invitations.MapPost("", async (Guid orgId, CreateInvitationRequest body, PrincipalAccessor principals, InvitationService service, HttpContext http, CancellationToken ct) =>
        {
            var grants = (body.Grants ?? throw BooksProblem.Invalid("grants is required; [] means no ledgers."))
                .Select(g => new GrantInput(g.LedgerId, MembershipEndpoints.ParseAccess(g.Access))).ToList();
            var command = new CreateInvitation(body.Email, MembershipEndpoints.ParseRole(body.Role), grants);
            return WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, command, ct));
        });

        invitations.MapGet("", async (Guid orgId, PrincipalAccessor principals, InvitationService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ct)));

        invitations.MapPost("/{invitationId:guid}/revoke", async (Guid orgId, Guid invitationId, PrincipalAccessor principals, InvitationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.RevokeAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, invitationId, ct)));

        api.MapGet("/me/invitations", async (PrincipalAccessor principals, InvitationService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.InboxAsync(await principals.GetAsync(ct), ct)));

        api.MapPost("/me/invitations/{invitationId:guid}/accept", async (Guid invitationId, PrincipalAccessor principals, InvitationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.AcceptAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), invitationId, ct)));
    }
}
