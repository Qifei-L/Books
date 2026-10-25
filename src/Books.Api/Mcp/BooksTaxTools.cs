using System.ComponentModel;
using Books.Api.Auth;
using Books.Api.Http;
using Books.Application.Documents;
using Books.Domain.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>
/// Tax return tools. The tax pack defines the return and the server calculates it, so AI clients read the same
/// definition and figures as people; they may draft definition versions, while filing and publishing stay with people.
/// </summary>
[McpServerToolType]
public sealed class BooksTaxTools(PrincipalAccessor principals, TaxReturnService returns, ReturnDefinitionService definitions, IHttpContextAccessor http)
{
    [McpServerTool(Name = "get_tax_pack", ReadOnly = true, Idempotent = true)]
    [Description("The ledger's tax pack: its tax currency, the current return definition's boxes and withholding categories.")]
    public Task<CallToolResult> Pack(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await returns.PackAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "get_tax_return", ReadOnly = true, Idempotent = true)]
    [Description("A month's tax return calculated from posted documents: boxes in USD and KHR, reconciliation with the tax accounts, "
        + "lines with overridden VAT, issues, and whether it was filed. Give khr_rate with khr_reason to see the month at one rate; "
        + "give definition_version to preview a draft definition.")]
    public Task<CallToolResult> Return(Guid org_id, Guid ledger_id, [Description("yyyy-MM")] string period,
        string? khr_rate = null, string? khr_reason = null, int? definition_version = null, CancellationToken ct = default) =>
        BooksReadTools.RunAsync(async () => await returns.ReturnAsync(await principals.GetAsync(ct), org_id, ledger_id, period,
            TaxReturnEndpoints.Override(khr_rate, khr_reason), ct, definition_version));

    [McpServerTool(Name = "get_tax_return_box_lines", ReadOnly = true, Idempotent = true)]
    [Description("The document lines that make up one box of a month's return, with their KHR rate and amount.")]
    public Task<CallToolResult> BoxLines(Guid org_id, Guid ledger_id, [Description("yyyy-MM")] string period, string box_key,
        int? definition_version = null, CancellationToken ct = default) =>
        BooksReadTools.RunAsync(async () => await returns.BoxLinesAsync(await principals.GetAsync(ct), org_id, ledger_id, period, box_key, null, ct, definition_version));

    [McpServerTool(Name = "list_tax_return_definitions", ReadOnly = true, Idempotent = true)]
    [Description("Versions of the ledger's return definition: 0 is the tax pack's built-in one; others are drafts or published from a month.")]
    public Task<CallToolResult> Definitions(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await definitions.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "save_tax_return_definition_draft", Idempotent = true)]
    [Description("Drafts a return definition version for a person to review and publish, for example after the tax office changes the form. "
        + "Start from list_tax_return_definitions. Line boxes pick lines by side (sale or purchase), tax_kinds (vat_output, vat_input, vat_input_non_deductible, "
        + "vat_zero_rated, non_vat, none) and withholding_category, and take amount net, tax or withholding; formula boxes add or subtract earlier "
        + "boxes (sign 1 or -1) with an optional percent. Keep existing keys for unchanged boxes. Omit number to create a draft; give number and "
        + "expected_version to change one. Preview it with get_tax_return and definition_version.")]
    public Task<CallToolResult> SaveDraft(
        Guid org_id, Guid ledger_id,
        [Description("A new UUID per intended change; reuse it to retry safely.")] Guid idempotency_key,
        [Description("First month the version applies to, yyyy-MM.")] string effective_from,
        ReturnDefinitionSpec definition,
        int? number = null, long? expected_version = null,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var principal = await principals.GetAsync(ct);
            var meta = McpWrites.Meta(http, "save_tax_return_definition_draft", idempotency_key, new { org_id, ledger_id, effective_from, definition, number }) with
            {
                ExpectedVersion = expected_version,
            };
            return number is { } n
                ? await definitions.UpdateDraftAsync(principal, meta, org_id, ledger_id, n, effective_from, definition, ct)
                : await definitions.CreateDraftAsync(principal, meta, org_id, ledger_id, effective_from, definition, ct);
        });
}
