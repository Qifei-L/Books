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
        [property: JsonPropertyName("item_id")] Guid? ItemId = null,
        [property: JsonPropertyName("quantity"), Description("Required for inventory items, as a decimal string.")] string? Quantity = null,
        [property: JsonPropertyName("unit"), Description("One of the item's units (list_items), for example box; omit for its base unit.")] string? Unit = null);

    [McpServerTool(Name = "list_purchase_invoices", ReadOnly = true, Idempotent = true)]
    [Description("Purchase invoices of a ledger with status (draft, posted, deleted), reasons a draft is not posted, and totals.")]
    public Task<CallToolResult> List(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await invoices.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    public sealed record PurchaseDocument(
        [property: JsonPropertyName("type"), Description("invoice, or debit_note for a supplier's credit.")] string Type,
        [property: JsonPropertyName("supplier_id"), Description("Supplier party ID from list_parties.")] Guid SupplierId,
        [property: JsonPropertyName("date"), Description("Document date, yyyy-MM-dd.")] DateOnly Date,
        [property: JsonPropertyName("lines")] PurchaseLine[] Lines,
        [property: JsonPropertyName("reference"), Description("The supplier's own document number.")] string? Reference = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("due_date"), Description("Omit for date + the supplier's payment terms.")] DateOnly? DueDate = null,
        [property: JsonPropertyName("original_invoice_id"), Description("Debit notes: the invoice being corrected, if known.")] Guid? OriginalInvoiceId = null,
        [property: JsonPropertyName("tax_currency_rate"), Description("Only to override the rate table, for example the KHR rate printed on the invoice.")] string? TaxCurrencyRate = null,
        [property: JsonPropertyName("tax_currency_reason"), Description("Required with tax_currency_rate.")] string? TaxCurrencyReason = null);

    [McpServerTool(Name = "save_purchase_drafts", Idempotent = true)]
    [Description("Saves up to 100 supplier invoices and debit notes as drafts for a person to review and post, all or none. "
        + "Amounts are net of VAT; set valid_tax_invoice only if a valid VAT tax invoice was received. If any document has an input error, "
        + "nothing is saved and every error is listed by document number; fix them and resend the whole batch with a new idempotency_key. "
        + "Saved drafts list issues that would block posting.")]
    public Task<CallToolResult> SaveDrafts(
        Guid org_id, Guid ledger_id,
        [Description("A new UUID per batch; reuse it to retry the same batch safely.")] Guid idempotency_key,
        PurchaseDocument[] documents,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var inputs = McpWrites.ParseAll(documents, d => (Input(d), d.Type switch
            {
                "invoice" => false,
                "debit_note" => true,
                _ => throw BooksProblem.Invalid("type must be invoice or debit_note."),
            }));
            var meta = McpWrites.Meta(http, "save_purchase_drafts", idempotency_key, new { org_id, ledger_id, documents });
            return await invoices.CreateDraftsAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, inputs, ct);
        });

    private static PurchaseInvoiceInput Input(PurchaseDocument d) =>
        PurchaseEndpoints.Input(new PurchaseEndpoints.InvoiceRequest(d.SupplierId, null, d.Date, d.DueDate, d.Reference, d.Description,
            (d.Lines ?? []).Select(l => new PurchaseEndpoints.LineRequest(l.AccountId, l.Description, l.ItemId, l.Quantity, null, l.Amount, l.TaxCodeId, null, null, l.ValidTaxInvoice, l.WithholdingCodeId, l.Unit)).ToList(),
            Post: false, d.OriginalInvoiceId, d.TaxCurrencyRate, d.TaxCurrencyReason));

    [McpServerTool(Name = "get_ap_aging", ReadOnly = true, Idempotent = true)]
    [Description("Accounts payable aging by supplier and days past due, with a check against the payable balance.")]
    public Task<CallToolResult> Aging(Guid org_id, Guid ledger_id, DateOnly as_of, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await payables.AgingAsync(await principals.GetAsync(ct), org_id, ledger_id, as_of, ct));

    [McpServerTool(Name = "get_supplier_statement", ReadOnly = true, Idempotent = true)]
    [Description("A supplier's statement: opening balance, invoices and debit notes with running balance.")]
    public Task<CallToolResult> Statement(Guid org_id, Guid ledger_id, Guid supplier_id, DateOnly from, DateOnly to, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await payables.StatementAsync(await principals.GetAsync(ct), org_id, ledger_id, supplier_id, from, to, ct));
}

/// <summary>Shared plumbing for MCP writes: request metadata from tool arguments, and results in the API's shape.</summary>
internal static class McpWrites
{
    public static WriteMeta Meta(IHttpContextAccessor http, string tool, Guid idempotencyKey, object arguments)
    {
        var fingerprint = JsonSerializer.Serialize(new { tool, arguments }, BooksJson.Options);
        return new WriteMeta(idempotencyKey, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint))), null,
            http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"));
    }

    /// <summary>Converts every document's arguments, reporting all format errors at once (the batch is all or none).</summary>
    public static List<T> ParseAll<TIn, T>(IReadOnlyList<TIn> documents, Func<TIn, T> parse)
    {
        var parsed = new List<T>();
        var errors = new List<BatchError>();
        for (var i = 0; i < documents.Count; i++)
        {
            try
            {
                parsed.Add(parse(documents[i]));
            }
            catch (BooksProblem problem) when (problem.Status == 400)
            {
                errors.Add(new BatchError(i + 1, problem.Code, problem.Message));
            }
        }
        return errors.Count == 0
            ? parsed
            : throw new BooksProblem(400, "BATCH_REJECTED", $"{errors.Count} of {documents.Count} documents have errors; nothing was saved.") { Issues = errors };
    }

    public static async Task<CallToolResult> RunAsync(Func<Task<WriteResult>> write)
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
