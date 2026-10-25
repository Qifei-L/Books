using System.ComponentModel;
using System.Text.Json.Serialization;
using Books.Api.Auth;
using Books.Api.Http;
using Books.Application.Common;
using Books.Application.Documents;
using Books.Application.Reporting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>Sales tools. AI clients prepare batches of draft invoices and credit notes; a person posts them.</summary>
[McpServerToolType]
public sealed class BooksSalesTools(PrincipalAccessor principals, SalesInvoiceService invoices, ReceivablesReportService receivables, IHttpContextAccessor http)
{
    public sealed record SalesLine(
        [property: JsonPropertyName("account_id"), Description("Income account from list_accounts.")] Guid AccountId,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("amount"), Description("Net amount, VAT excluded, as a decimal string.")] string Amount,
        [property: JsonPropertyName("tax_code_id"), Description("Output VAT, VAT 0% or Non-VAT code from list_tax_codes.")] Guid? TaxCodeId = null,
        [property: JsonPropertyName("item_id")] Guid? ItemId = null,
        [property: JsonPropertyName("other_tax_code_id"), Description("Specific, accommodation or public lighting tax code, charged on top of VAT.")] Guid? OtherTaxCodeId = null,
        [property: JsonPropertyName("quantity"), Description("Required for inventory items, as a decimal string.")] string? Quantity = null,
        [property: JsonPropertyName("unit"), Description("One of the item's units (list_items), for example box; omit for its base unit.")] string? Unit = null);

    public sealed record SalesDocument(
        [property: JsonPropertyName("type"), Description("invoice, or credit_note for a credit to the customer.")] string Type,
        [property: JsonPropertyName("customer_id"), Description("Customer party ID from list_parties.")] Guid CustomerId,
        [property: JsonPropertyName("date"), Description("Document date, yyyy-MM-dd.")] DateOnly Date,
        [property: JsonPropertyName("lines")] SalesLine[] Lines,
        [property: JsonPropertyName("reference"), Description("Your own document number, if any.")] string? Reference = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("due_date"), Description("Omit for date + the customer's payment terms.")] DateOnly? DueDate = null,
        [property: JsonPropertyName("original_invoice_id"), Description("Credit notes: the invoice being corrected, if known.")] Guid? OriginalInvoiceId = null,
        [property: JsonPropertyName("tax_currency_rate"), Description("Only to override the KHR rate from the rate table.")] string? TaxCurrencyRate = null,
        [property: JsonPropertyName("tax_currency_reason"), Description("Required with tax_currency_rate.")] string? TaxCurrencyReason = null);

    [McpServerTool(Name = "list_sales_invoices", ReadOnly = true, Idempotent = true)]
    [Description("Sales invoices and credit notes of a ledger with status (draft, posted, deleted), reasons a draft is not posted, totals and KHR totals.")]
    public Task<CallToolResult> List(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () =>
        {
            var principal = await principals.GetAsync(ct);
            return (await invoices.ListAsync(principal, org_id, ledger_id, ct)).Concat(await invoices.ListAsync(principal, org_id, ledger_id, ct, creditNotes: true)).ToList();
        });

    [McpServerTool(Name = "save_sales_drafts", Idempotent = true)]
    [Description("Saves up to 100 sales invoices and credit notes as drafts for a person to review and post, all or none. "
        + "Amounts are net of VAT. If any document has an input error, nothing is saved and every error is listed by document number; "
        + "fix them and resend the whole batch with a new idempotency_key. Saved drafts list issues that would block posting.")]
    public Task<CallToolResult> SaveDrafts(
        Guid org_id, Guid ledger_id,
        [Description("A new UUID per batch; reuse it to retry the same batch safely.")] Guid idempotency_key,
        SalesDocument[] documents,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var inputs = McpWrites.ParseAll(documents, d => (Input(d), d.Type switch
            {
                "invoice" => false,
                "credit_note" => true,
                _ => throw BooksProblem.Invalid("type must be invoice or credit_note."),
            }));
            var meta = McpWrites.Meta(http, "save_sales_drafts", idempotency_key, new { org_id, ledger_id, documents });
            return await invoices.CreateDraftsAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, inputs, ct);
        });

    [McpServerTool(Name = "get_ar_aging", ReadOnly = true, Idempotent = true)]
    [Description("Accounts receivable aging by customer and days past due, with a check against the receivable balance.")]
    public Task<CallToolResult> Aging(Guid org_id, Guid ledger_id, DateOnly as_of, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await receivables.AgingAsync(await principals.GetAsync(ct), org_id, ledger_id, as_of, ct));

    [McpServerTool(Name = "get_customer_statement", ReadOnly = true, Idempotent = true)]
    [Description("A customer's statement: opening balance, invoices and credit notes with running balance.")]
    public Task<CallToolResult> Statement(Guid org_id, Guid ledger_id, Guid customer_id, DateOnly from, DateOnly to, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await receivables.StatementAsync(await principals.GetAsync(ct), org_id, ledger_id, customer_id, from, to, ct));

    private static SalesInvoiceInput Input(SalesDocument d) =>
        SalesEndpoints.Input(new SalesEndpoints.InvoiceRequest(d.CustomerId, null, d.Date, d.DueDate, d.Reference, d.Description,
            (d.Lines ?? []).Select(l => new SalesEndpoints.LineRequest(l.AccountId, l.Description, l.ItemId, l.Quantity, null, l.Amount, l.TaxCodeId, null, null, l.OtherTaxCodeId, l.Unit)).ToList(),
            Post: false, d.OriginalInvoiceId, d.TaxCurrencyRate, d.TaxCurrencyReason));
}
