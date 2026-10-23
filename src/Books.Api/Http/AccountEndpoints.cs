using System.Text.Json;
using Books.Api.Auth;
using Books.Application.Common;
using Books.Application.Ledgers;
using Books.Domain.Ledgers;

namespace Books.Api.Http;

public static class AccountEndpoints
{
    public sealed record CreateAccountRequest(
        string Code, string Name, string Type, Guid? GroupId, bool? IsControl, bool? IsBank, string? Currency);

    public sealed record CreateGroupRequest(string Name, string Type, Guid? ParentId);

    public static void MapAccountEndpoints(this RouteGroupBuilder api)
    {
        var groups = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/groups");

        groups.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, AccountGroupService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        groups.MapPost("", async (Guid orgId, Guid ledgerId, CreateGroupRequest body, PrincipalAccessor principals, AccountGroupService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.CreateAsync(
                await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, body.Name, ParseType(body.Type), body.ParentId, ct)));

        groups.MapPatch("/{groupId:guid}", async (Guid orgId, Guid ledgerId, Guid groupId, JsonElement body, PrincipalAccessor principals, AccountGroupService service, HttpContext http, CancellationToken ct) =>
        {
            var fields = new PatchFields(body, "name", "parent_id");
            var command = new UpdateAccountGroup(fields.Text("name"), fields.Has("parent_id"), fields.Guid("parent_id"));
            return WriteHttp.Result(http, await service.UpdateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, groupId, command, ct));
        });

        groups.MapDelete("/{groupId:guid}", async (Guid orgId, Guid ledgerId, Guid groupId, PrincipalAccessor principals, AccountGroupService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, groupId, ct)));

        var accounts = api.MapGroup("/organizations/{orgId:guid}/ledgers/{ledgerId:guid}/accounts");

        accounts.MapGet("", async (Guid orgId, Guid ledgerId, PrincipalAccessor principals, AccountService service, HttpContext http, CancellationToken ct) =>
            Envelope.Ok(http, await service.ListAsync(await principals.GetAsync(ct), orgId, ledgerId, ct)));

        accounts.MapPost("", async (Guid orgId, Guid ledgerId, CreateAccountRequest body, PrincipalAccessor principals, AccountService service, HttpContext http, CancellationToken ct) =>
        {
            var command = new CreateAccount(body.Code, body.Name, ParseType(body.Type), body.GroupId, body.IsControl ?? false, body.IsBank ?? false, body.Currency);
            return WriteHttp.Result(http, await service.CreateAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, command, ct));
        });

        accounts.MapPatch("/{accountId:guid}", async (Guid orgId, Guid ledgerId, Guid accountId, JsonElement body, PrincipalAccessor principals, AccountService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.UpdateAsync(
                await principals.GetAsync(ct), WriteHttp.Meta(http, body), orgId, ledgerId, accountId, ParseUpdate(body), ct)));

        accounts.MapDelete("/{accountId:guid}", async (Guid orgId, Guid ledgerId, Guid accountId, PrincipalAccessor principals, AccountService service, HttpContext http, CancellationToken ct) =>
            WriteHttp.Result(http, await service.DeleteAsync(await principals.GetAsync(ct), WriteHttp.Meta(http, null), orgId, ledgerId, accountId, ct)));
    }

    internal static UpdateAccount ParseUpdate(JsonElement body)
    {
        var fields = new PatchFields(body, "code", "name", "type", "active", "group_id");
        return new UpdateAccount(
            fields.Text("code"),
            fields.Text("name"),
            fields.Text("type") is { } type ? ParseType(type) : null,
            fields.Bool("active"),
            fields.Has("group_id"),
            fields.Guid("group_id"));
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

/// <summary>
/// PATCH semantics: an omitted field is unchanged, unknown fields are rejected, and only ID references
/// (parent_id, group_id) may be null, meaning "none".
/// </summary>
internal sealed class PatchFields
{
    private readonly JsonElement _body;

    public PatchFields(JsonElement body, params string[] allowed)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw BooksProblem.Invalid("The body must be a JSON object.");
        }
        if (body.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => !allowed.Contains(name)) is { } unknown)
        {
            throw BooksProblem.Invalid($"Unknown field: {unknown}.");
        }
        _body = body;
    }

    public bool Has(string name) => _body.TryGetProperty(name, out _);

    public string? Text(string name) =>
        _body.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : throw BooksProblem.Invalid($"{name} must be a string.")
            : null;

    public bool? Bool(string name) =>
        _body.TryGetProperty(name, out var v)
            ? v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw BooksProblem.Invalid($"{name} must be true or false.")
            : null;

    public Guid? Guid(string name) =>
        !_body.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null
            ? null
            : v.ValueKind == JsonValueKind.String && v.TryGetGuid(out var id) ? id : throw BooksProblem.Invalid($"{name} must be a UUID or null.");
}
