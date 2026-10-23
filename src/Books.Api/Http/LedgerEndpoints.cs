using Books.Api.Auth;
using Books.Application.Ledgers;

namespace Books.Api.Http;

public static class LedgerEndpoints
{
    public sealed record CreateCompanyRequest(string Name, string? BaseCurrency, int? FyStartMonth);

    public sealed record NameRequest(string Name);

    public static void MapLedgerEndpoints(this RouteGroupBuilder api)
    {
        var org = api.MapGroup("/organizations/{orgId:guid}");

        org.MapPost("/companies", async (Guid orgId, CreateCompanyRequest body, PrincipalAccessor principals, CompanyService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(
                await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, new CreateCompany(body.Name, body.BaseCurrency, body.FyStartMonth), ct)));

        org.MapGet("/companies", async (Guid orgId, PrincipalAccessor principals, CompanyService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ct)));

        org.MapGet("/companies/{companyId:guid}", async (Guid orgId, Guid companyId, PrincipalAccessor principals, CompanyService service, HttpContext http, CancellationToken ct) =>
        {
            var company = await service.GetAsync(await principals.GetAsync(ct), orgId, companyId, ct);
            return Envelope.Ok(http, company, company.Version);
        });

        org.MapPatch("/companies/{companyId:guid}", async (Guid orgId, Guid companyId, NameRequest body, PrincipalAccessor principals, CompanyService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.RenameAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, companyId, body.Name, ct)));

        org.MapGet("/ledgers", async (Guid orgId, PrincipalAccessor principals, CompanyService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListLedgersAsync(await principals.GetAsync(ct), orgId, ct)));

        org.MapGet("/ledgers/{ledgerId:guid}", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, CompanyService service, HttpContext http, CancellationToken ct) =>
        {
            var ledger = await service.GetLedgerAsync(await principals.GetAsync(ct), orgId, ledgerId, ct);
            return Envelope.Ok(http, ledger, ledger.Version);
        });
    }
}
