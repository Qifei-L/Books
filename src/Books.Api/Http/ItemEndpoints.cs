using Books.Api.Auth;
using Books.Application.Ledgers;

namespace Books.Api.Http;

public static class ItemEndpoints
{
    public sealed record CategoryRequest(string Name);

    public sealed record ItemRequest(string Name, string? Description, string? Unit, Guid? CategoryId, Guid? DefaultAccountId, Guid? DefaultTaxCodeId, bool? Active,
        bool? TrackInventory = null, Guid? InventoryAccountId = null, Guid? IncomeAccountId = null, Guid? CostOfSalesAccountId = null);

    public static void MapItemEndpoints(this RouteGroupBuilder api)
    {
        var ledger = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}");

        ledger.MapGet("/item-categories", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, ItemService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListCategoriesAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        ledger.MapPost("/item-categories", async (Guid orgId, Guid ledgerId, CategoryRequest body, PrincipalAccessor principals, ItemService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateCategoryAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, body.Name, ct)));

        ledger.MapGet("/items", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, ItemService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        ledger.MapPost("/items", async (Guid orgId, Guid ledgerId, ItemRequest body, PrincipalAccessor principals, ItemService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, Input(body), ct)));

        ledger.MapPut("/items/{itemId:guid}", async (Guid orgId, Guid ledgerId, Guid itemId, ItemRequest body, PrincipalAccessor principals, ItemService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, itemId, Input(body), ct)));
    }

    private static ItemInput Input(ItemRequest body) =>
        new(body.Name, body.Description, body.Unit, body.CategoryId, body.DefaultAccountId, body.DefaultTaxCodeId, body.Active ?? true, body.TrackInventory ?? false,
            body.InventoryAccountId, body.IncomeAccountId, body.CostOfSalesAccountId);
}
