using System.Text.Json;
using Books.Api.Auth;
using Books.Application.Ledgers;

namespace Books.Api.Http;

public static class PartyEndpoints
{
    private static readonly string[] Fields =
        ["name", "tax_id", "is_customer", "is_supplier", "email", "phone", "address", "contact_name", "payment_terms_days", "active"];

    public static void MapPartyEndpoints(this RouteGroupBuilder api)
    {
        var parties = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/parties");

        parties.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, PartyService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        parties.MapPost("", async (Guid orgId, Guid ledgerId, JsonElement body, PrincipalAccessor principals, PartyService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Parse(body), ct)));

        parties.MapPatch("/{partyId:guid}", async (Guid orgId, Guid ledgerId, Guid partyId, JsonElement body, PrincipalAccessor principals, PartyService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, partyId, Parse(body), ct)));

        parties.MapDelete("/{partyId:guid}", async (Guid orgId, Guid ledgerId, Guid partyId, PrincipalAccessor principals, PartyService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, partyId, ct)));
    }

    private static PartyInput Parse(JsonElement body)
    {
        var f = new PatchFields(body, Fields);
        return new PartyInput(f.Text("name"), f.Text("tax_id"), f.Bool("is_customer"), f.Bool("is_supplier"),
            f.Text("email"), f.Text("phone"), f.Text("address"), f.Text("contact_name"), f.Int("payment_terms_days"), f.Bool("active"));
    }
}
