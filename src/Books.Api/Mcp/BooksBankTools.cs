using System.ComponentModel;
using System.Text.Json.Serialization;
using Books.Api.Auth;
using Books.Api.Http;
using Books.Application.Common;
using Books.Application.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>
/// Payments, receipts and bank statements. AI clients draft payments and receipts, import statements (which clear
/// matching postings and draft the rest to Suspense) and clear postings; people post.
/// </summary>
[McpServerToolType]
public sealed class BooksBankTools(
    PrincipalAccessor principals, PaymentService payments, BankStatementService statements, BankReconciliationService reconciliation, IHttpContextAccessor http)
{
    public sealed record PaymentLine(
        [property: JsonPropertyName("amount"), Description("Decimal string; VAT excluded on account lines; may be negative (fees, discounts, tax a customer withheld).")] string Amount,
        [property: JsonPropertyName("account_id"), Description("The account. An account with the Payable or Receivable usage settles with the party; omit to settle on the document's own side.")] Guid? AccountId = null,
        [property: JsonPropertyName("invoice_id"), Description("The invoice this line settles, if known.")] Guid? InvoiceId = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("tax_code_id"), Description("Account lines: VAT code.")] Guid? TaxCodeId = null,
        [property: JsonPropertyName("valid_tax_invoice"), Description("Payments with input VAT: true only if a valid tax invoice was received.")] bool ValidTaxInvoice = true,
        [property: JsonPropertyName("withholding_code_id"), Description("Payments: WHT code when tax is withheld.")] Guid? WithholdingCodeId = null);

    public sealed record PaymentDocument(
        [property: JsonPropertyName("type"), Description("payment (money out) or receipt (money in).")] string Type,
        [property: JsonPropertyName("bank_account_id"), Description("Bank or cash account from list_bank_accounts.")] Guid BankAccountId,
        [property: JsonPropertyName("date"), Description("yyyy-MM-dd")] DateOnly Date,
        [property: JsonPropertyName("lines")] PaymentLine[] Lines,
        [property: JsonPropertyName("party_id"), Description("Supplier or customer; required to settle invoices or balances.")] Guid? PartyId = null,
        [property: JsonPropertyName("reference")] string? Reference = null,
        [property: JsonPropertyName("description")] string? Description = null);

    public sealed record StatementLine(
        [property: JsonPropertyName("date"), Description("yyyy-MM-dd")] DateOnly Date,
        [property: JsonPropertyName("amount"), Description("Decimal string: positive for money in, negative for money out.")] string Amount,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("reference")] string? Reference = null,
        [property: JsonPropertyName("external_id"), Description("The bank's own transaction ID, if shown; used to skip lines imported before.")] string? ExternalId = null);

    [McpServerTool(Name = "list_payments", ReadOnly = true, Idempotent = true)]
    [Description("Payments and receipts of a ledger with their lines, status and reasons a draft is not posted.")]
    public Task<CallToolResult> List(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () =>
        {
            var principal = await principals.GetAsync(ct);
            return (await payments.ListAsync(principal, org_id, ledger_id, false, ct)).Concat(await payments.ListAsync(principal, org_id, ledger_id, true, ct)).ToList();
        });

    [McpServerTool(Name = "save_payment_drafts", Idempotent = true)]
    [Description("Saves up to 100 payments and receipts as drafts for a person to post, all or none. Each document has many lines, Manager.io style: "
        + "a line on the Payable/Receivable account (or with no account) settles with the party, optionally naming an invoice; other lines post to "
        + "their account with optional VAT and withholding; use negative lines for fees, discounts and tax a customer withheld.")]
    public Task<CallToolResult> SaveDrafts(
        Guid org_id, Guid ledger_id,
        [Description("A new UUID per batch; reuse it to retry the same batch safely.")] Guid idempotency_key,
        PaymentDocument[] documents,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var inputs = McpWrites.ParseAll(documents, d => (PaymentEndpoints.Input(new PaymentEndpoints.PaymentRequest(d.BankAccountId, d.PartyId, d.Date, d.Reference, d.Description,
                (d.Lines ?? []).Select(l => new PaymentEndpoints.LineRequest(l.InvoiceId, l.AccountId, l.Description, null, null, null, l.Amount, l.TaxCodeId, null, null,
                    l.ValidTaxInvoice, l.WithholdingCodeId)).ToList(), Post: false)),
                d.Type switch
                {
                    "payment" => false,
                    "receipt" => true,
                    _ => throw BooksProblem.Invalid("type must be payment or receipt."),
                }));
            var meta = McpWrites.Meta(http, "save_payment_drafts", idempotency_key, new { org_id, ledger_id, documents });
            return await payments.CreateDraftsAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, inputs, ct);
        });

    [McpServerTool(Name = "import_bank_statement", Idempotent = true)]
    [Description("Imports a bank statement read from the bank's file or PDF. Opening balance plus the lines must equal the closing balance, or "
        + "nothing is saved. Lines imported before are skipped; a line with the same amount as a pending posting within five days clears it; "
        + "other lines become draft receipts or payments to Suspense for a person to post and the accountant or AI to reclassify.")]
    public Task<CallToolResult> ImportStatement(
        Guid org_id, Guid ledger_id,
        [Description("A new UUID per statement; reuse it to retry safely.")] Guid idempotency_key,
        [Description("The bank or cash account.")] Guid account_id,
        [Description("Decimal string.")] string opening_balance,
        [Description("Decimal string.")] string closing_balance,
        StatementLine[] lines,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var (opening, closing, parsed) = BankEndpoints.Statement(new BankEndpoints.StatementRequest(opening_balance, closing_balance,
                (lines ?? []).Select(l => new BankEndpoints.StatementLineRequest(l.Date, l.Description, l.Reference, l.Amount, l.ExternalId)).ToList()));
            var meta = McpWrites.Meta(http, "import_bank_statement", idempotency_key, new { org_id, ledger_id, account_id, opening_balance, closing_balance, lines });
            return await statements.ImportAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, account_id, opening, closing, parsed, ct);
        });

    [McpServerTool(Name = "get_bank_reconciliation", ReadOnly = true, Idempotent = true)]
    [Description("A bank account's reconciliation on a date: book balance, cleared balance, the difference from the statement balance if given, and pending postings.")]
    public Task<CallToolResult> Reconciliation(Guid org_id, Guid ledger_id, Guid account_id, DateOnly as_of, string? statement_balance = null, CancellationToken ct = default) =>
        BooksReadTools.RunAsync(async () => await reconciliation.ReconciliationAsync(await principals.GetAsync(ct), org_id, ledger_id, account_id, as_of,
            PurchaseEndpoints.Number(statement_balance, 0, "statement_balance", signed: true), ct));

    [McpServerTool(Name = "clear_bank_postings", Idempotent = true)]
    [Description("Marks postings as cleared on a statement date, for matches the statement import did not make (different dates or split amounts).")]
    public Task<CallToolResult> Clear(Guid org_id, Guid ledger_id,
        [Description("A new UUID per change.")] Guid idempotency_key, Guid account_id, DateOnly cleared_on, Guid[] journal_ids, CancellationToken ct = default) =>
        McpWrites.RunAsync(async () => await reconciliation.ClearAsync(await principals.GetAsync(ct),
            McpWrites.Meta(http, "clear_bank_postings", idempotency_key, new { org_id, ledger_id, account_id, cleared_on, journal_ids }),
            org_id, ledger_id, account_id, cleared_on, journal_ids, ct));
}
