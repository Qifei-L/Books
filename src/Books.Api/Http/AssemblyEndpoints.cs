using Books.Api.Auth;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class AssemblyEndpoints
{
    /// <param name="Quantity">How many of the item to make, in its base unit.</param>
    /// <param name="Labour">Labour added to the cost, as a decimal string; needs labour_account_id.</param>
    public sealed record AssemblyRequest(DateOnly Date, Guid ItemId, string Quantity, string? Labour, Guid? LabourAccountId, string? Reference, string? Description,
        Guid? LocationId = null);

    public static void MapAssemblyEndpoints(this RouteGroupBuilder api)
    {
        var assemblies = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/assemblies");

        assemblies.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, AssemblyService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));
        assemblies.MapPost("", async (Guid orgId, Guid ledgerId, AssemblyRequest body, PrincipalAccessor principals, AssemblyService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId,
                new AssemblyInput(body.Date, body.ItemId, PurchaseEndpoints.Number(body.Quantity, 0, "quantity") ?? 0, PurchaseEndpoints.Number(body.Labour, 0, "labour") ?? 0,
                    body.LabourAccountId, body.Reference, body.Description, body.LocationId), ct)));
        assemblies.MapDelete("/{assemblyId:guid}", async (Guid orgId, Guid ledgerId, Guid assemblyId, PrincipalAccessor principals, AssemblyService service, HttpContext http,
            CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, assemblyId, ct)));
    }
}
