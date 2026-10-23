using Books.Api.Auth;
using Books.Application.Access;

namespace Books.Api.Http;

public static class OrganizationEndpoints
{
    public sealed record NameRequest(string Name);

    public static void MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/organizations", async (NameRequest body, PrincipalAccessor principals, OrganizationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), body.Name, ct)));

        api.MapGet("/me/organizations", async (PrincipalAccessor principals, OrganizationService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListMineAsync(await principals.GetAsync(ct), ct)));

        api.MapGet("/organizations/{orgId:guid}", async (Guid orgId, PrincipalAccessor principals, OrganizationService service, HttpContext http, CancellationToken ct) =>
        {
            var organization = await service.GetAsync(await principals.GetAsync(ct), orgId, ct);
            return Envelope.Ok(http, organization, organization.Version);
        });

        api.MapPatch("/organizations/{orgId:guid}", async (Guid orgId, NameRequest body, PrincipalAccessor principals, OrganizationService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.RenameAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, body.Name, ct)));
    }
}
