using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Ledgers;

namespace Books.Api.Http;

public static class ClassEndpoints
{
    public sealed record ClassRequest(string Name, string? Code, bool? Active);

    public static void MapClassEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/classes", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, ClassService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        ledger.MapPost("/classes", async (Guid orgId, Guid ledgerId, ClassRequest body, PrincipalAccessor principals, ClassService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, null, Input(body), ct)));

        ledger.MapPut("/classes/{classId:guid}", async (Guid orgId, Guid ledgerId, Guid classId, ClassRequest body, PrincipalAccessor principals, ClassService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.SaveAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, classId, Input(body), ct)));
    }

    private static ClassInput Input(ClassRequest body) => new(body.Name, body.Code, body.Active);
}
