using System.ComponentModel;
using Books.Api.Auth;
using Books.Application.Documents;
using Books.Application.Ledgers;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>Read-only tools for ledger setup data an AI needs to prepare journals: parties, tax codes, banks, items.</summary>
[McpServerToolType]
public sealed class BooksSetupTools(
    PrincipalAccessor principals, PartyService parties, TaxCodeService taxCodes, BankService banks, ItemService items, BankTransferService transfers)
{
    [McpServerTool(Name = "list_parties", ReadOnly = true, Idempotent = true)]
    [Description("Customers and suppliers of a ledger, with tax ID and payment terms.")]
    public Task<CallToolResult> ListParties(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await parties.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "list_tax_codes", ReadOnly = true, Idempotent = true)]
    [Description("Tax codes of a ledger: kind (vat_output, vat_input, vat_zero_rated, non_vat, withholding), rate, account and effective dates.")]
    public Task<CallToolResult> ListTaxCodes(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await taxCodes.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "list_bank_accounts", ReadOnly = true, Idempotent = true)]
    [Description("Bank accounts of a ledger and the ledger account each one posts to.")]
    public Task<CallToolResult> ListBankAccounts(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await banks.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "list_items", ReadOnly = true, Idempotent = true)]
    [Description("Items of a ledger with category, unit, and the default account and tax code they suggest.")]
    public Task<CallToolResult> ListItems(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await items.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "list_transfers", ReadOnly = true, Idempotent = true)]
    [Description("Bank transfers of a ledger with their journal numbers.")]
    public Task<CallToolResult> ListTransfers(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await transfers.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));
}
