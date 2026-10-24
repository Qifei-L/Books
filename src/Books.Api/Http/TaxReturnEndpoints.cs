using Books.Api.Auth;
using Books.Application.Documents;

namespace Books.Api.Http;

public static class TaxReturnEndpoints
{
    public static void MapTaxReturnEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/tax-pack", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, TaxReturnService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.PackAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));
    }
}
