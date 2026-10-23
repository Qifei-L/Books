using System.Text.Json;
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

        accounts.MapPatch("/{accountId:guid}", async (Guid orgId, Guid ledgerId, Guid accountId, JsonElement body, PrincipalAccessor principals, AccountService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(
                await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, accountId, ParseUpdate(body), ct)));
    }

    /// <summary>PATCH semantics: an omitted field is unchanged; only parent_id may be null (move to the top level).</summary>
    internal static UpdateAccount ParseUpdate(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw BooksProblem.Invalid("The body must be a JSON object.");
        }
        string[] allowed = ["code", "name", "type", "active", "parent_id"];
        if (body.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => !allowed.Contains(name)) is { } unknown)
        {
            throw BooksProblem.Invalid($"Unknown field: {unknown}.");
        }
        try
        {
            string? Text(string name) => body.TryGetProperty(name, out var v) ? v.GetString() ?? throw BooksProblem.Invalid($"{name} cannot be null.") : null;
            var parentChanges = body.TryGetProperty("parent_id", out var parent);
            return new UpdateAccount(
                Text("code"),
                Text("name"),
                Text("type") is { } type ? ParseType(type) : null,
                body.TryGetProperty("active", out var active) ? active.GetBoolean() : null,
                parentChanges,
                parentChanges && parent.ValueKind != JsonValueKind.Null ? parent.GetGuid() : null);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException)
        {
            throw BooksProblem.Invalid("A field has the wrong type.");
        }
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
