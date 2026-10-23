using System.ComponentModel;
using Books.Api.Auth;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Ledgers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>
/// Read-only MCP tools. Each tool resolves the verified caller from the bearer token and calls the same
/// application service as the HTTP API, so results and permission errors are identical.
/// </summary>
[McpServerToolType]
public sealed class BooksReadTools(PrincipalAccessor principals, OrganizationService organizations, CompanyService companies, AccountService accounts)
{
    [McpServerTool(Name = "list_organizations", ReadOnly = true, Idempotent = true)]
    [Description("Organizations where the signed-in person is currently an active member, with their role in each.")]
    public Task<CallToolResult> ListOrganizations(CancellationToken ct) =>
        RunAsync(async () => await organizations.ListMineAsync(await principals.GetAsync(ct), ct));

    [McpServerTool(Name = "list_ledgers", ReadOnly = true, Idempotent = true)]
    [Description("Ledgers in one organization that the signed-in person can access, with base currency, fiscal year start and access level.")]
    public Task<CallToolResult> ListLedgers([Description("Organization ID from list_organizations.")] Guid org_id, CancellationToken ct) =>
        RunAsync(async () => await companies.ListLedgersAsync(await principals.GetAsync(ct), org_id, ct));

    [McpServerTool(Name = "list_accounts", ReadOnly = true, Idempotent = true)]
    [Description("Chart of accounts of one ledger, ordered by code. Only accounts with postable = true can take postings.")]
    public Task<CallToolResult> ListAccounts(
        [Description("Organization ID from list_organizations.")] Guid org_id,
        [Description("Ledger ID from list_ledgers.")] Guid ledger_id,
        CancellationToken ct) =>
        RunAsync(async () => await accounts.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    /// <summary>Returns {"data": …} like the API; a business error becomes an error result with the API's code.</summary>
    private static async Task<CallToolResult> RunAsync<T>(Func<Task<T>> read)
    {
        try
        {
            var data = await read();
            return new CallToolResult { Content = [new TextContentBlock { Text = BooksJson.Serialize(new { data }) }] };
        }
        catch (BooksProblem problem)
        {
            var error = BooksJson.Serialize(new { status = problem.Status, code = problem.Code, detail = problem.Message });
            return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = error }] };
        }
    }
}
