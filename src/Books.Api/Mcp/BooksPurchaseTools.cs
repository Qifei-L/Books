using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Books.Api.Auth;
using Books.Api.Http;
using Books.Application.Common;
using Books.Application.Documents;
using Books.Application.Reporting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>Purchase tools. AI clients prepare draft invoices; a person posts them (owner decision).</summary>
[McpServerToolType]
public sealed class BooksPurchaseTools(PrincipalAccessor principals, PurchaseInvoiceService invoices, PayablesReportService payables, IHttpContextAccessor http)
{
    public sealed record PurchaseLine(
        [property: JsonPropertyName("account_id")] Guid AccountId,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("amount"), Description("Net amount, VAT excluded, as a decimal string.")] string Amount,
        [property: JsonPropertyName("tax_code_id"), Description("Input VAT, VAT 0% or Non-VAT code from list_tax_codes.")] Guid? TaxCodeId,
        [property: JsonPropertyName("valid_tax_invoice"), Description("True only when a valid VAT tax invoice was received; otherwise VAT is non-deductible.")] bool ValidTaxInvoice,
        [property: JsonPropertyName("withholding_code_id"), Description("WHT code when tax must be withheld from this line.")] Guid? WithholdingCodeId = null,
        [property: JsonPropertyName("item_id")] Guid? ItemId = null);

    [McpServerTool(Name = "list_purchase_invoices", ReadOnly = true, Idempotent = true)]
    [Description("Purchase invoices of a ledger with status (draft, posted, deleted), reasons a draft is not posted, and totals.")]
    public Task<CallToolResult> List(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await invoices.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "save_purchase_invoice_draft", Idempotent = true)]
    [Description("Saves a supplier invoice as a draft for a person to review and post. Read the supplier invoice carefully: "
        + "amounts are net of VAT; set valid_tax_invoice only if a valid VAT tax invoice was received. The result lists issues that would block posting.")]
    public Task<CallToolResult> SaveDraft(
        Guid org_id, Guid ledger_id,
        [Description("A new UUID per intended change; reuse it to retry safely.")] Guid idempotency_key,
        [Description("Supplier party ID from list_parties.")] Guid supplier_id,
        [Description("Invoice date, yyyy-MM-dd.")] DateOnly date,
        PurchaseLine[] lines,
        [Description("The supplier's invoice number.")] string? reference = null,
        string? description = null,
        CancellationToken ct = default) =>
        WriteAsync(async () =>
        {
            var request = new PurchaseEndpoints.InvoiceRequest(supplier_id, null, date, null, reference, description,
                lines.Select(l => new PurchaseEndpoints.LineRequest(l.AccountId, l.Description, l.ItemId, null, null, l.Amount, l.TaxCodeId, null, null, l.ValidTaxInvoice, l.WithholdingCodeId)).ToList(),
                Post: false);
            var fingerprint = JsonSerializer.Serialize(new { tool = "save_purchase_invoice_draft", org_id, ledger_id, request }, BooksJson.Options);
            var meta = new WriteMeta(idempotency_key, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))), null,
                http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"));
            return await invoices.CreateAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, PurchaseEndpoints.Input(request), post: false, ct);
        });

    [McpServerTool(Name = "get_ap_aging", ReadOnly = true, Idempotent = true)]
    [Description("Accounts payable aging by supplier and days past due, with a check against the payable balance.")]
    public Task<CallToolResult> Aging(Guid org_id, Guid ledger_id, DateOnly as_of, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await payables.AgingAsync(await principals.GetAsync(ct), org_id, ledger_id, as_of, ct));

    [McpServerTool(Name = "get_supplier_statement", ReadOnly = true, Idempotent = true)]
    [Description("A supplier's statement: opening balance, invoices and debit notes with running balance.")]
    public Task<CallToolResult> Statement(Guid org_id, Guid ledger_id, Guid supplier_id, DateOnly from, DateOnly to, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await payables.StatementAsync(await principals.GetAsync(ct), org_id, ledger_id, supplier_id, from, to, ct));

    private static async Task<CallToolResult> WriteAsync(Func<Task<WriteResult>> write)
    {
        try
        {
            var result = await write();
            return new CallToolResult { IsError = result.Status >= 400, Content = [new TextContentBlock { Text = $"{{\"status\":{result.Status},\"data\":{result.Json}}}" }] };
        }
        catch (BooksProblem problem)
        {
            return BooksReadTools.Problem(problem);
        }
        catch (Books.Domain.Access.DomainException domain)
        {
            return BooksReadTools.Problem(BooksProblem.Invalid(domain.Message));
        }
    }
}
