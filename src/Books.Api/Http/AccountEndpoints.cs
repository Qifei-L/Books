using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Ledgers;
using Books.Domain.Ledgers;

namespace Books.Api.Http;

public static class AccountEndpoints
{
    public sealed record CreateAccountRequest(
        string Code, string Name, string Type, Guid? ParentId, bool? IsControl, bool? IsBank, string? Currency);

    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/accounts");

        accounts.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, AccountService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        accounts.MapPost("", async (Guid orgId, Guid ledgerId, CreateAccountRequest body, PrincipalAccessor principals, AccountService service, HttpContext http, CancellationToken ct) =>
        {
            var command = new CreateAccount(body.Code, body.Name, ParseType(body.Type), body.ParentId, body.IsControl ?? false, body.IsBank ?? false, body.Currency);
            return WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, command, ct));
        });
    }

    internal static AccountType ParseType(string? type) => type switch
    {
        "assets" => AccountType.Assets,
        "liabilities" => AccountType.Liabilities,
        "equity" => AccountType.Equity,
        "income" => AccountType.Income,
        "cost_and_expense" => AccountType.CostAndExpense,
        _ => throw BooksProblem.Invalid("type must be assets, liabilities, equity, income or cost_and_expense."),
    };
}
