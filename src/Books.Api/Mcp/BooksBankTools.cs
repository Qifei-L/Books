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
    PrincipalAccessor principals, PaymentService payments, BankStatementService statements, BankReconciliationService reconciliation, PayRunService payRuns,
    FixedAssetService assets, InventoryReportService inventory, StockAdjustmentService adjustments, IHttpContextAccessor http)
{
    public sealed record PaymentLine(
        [property: JsonPropertyName("amount"), Description("Decimal string; VAT excluded on account lines; may be negative (fees, discounts, tax a customer withheld).")] string Amount,
        [property: JsonPropertyName("account_id"), Description("The account. An account with the Payable or Receivable usage settles with the party; omit to settle on the document's own side.")] Guid? AccountId = null,
        [property: JsonPropertyName("invoice_id"), Description("The invoice this line settles, if known.")] Guid? InvoiceId = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("tax_code_id"), Description("Account lines: VAT code.")] Guid? TaxCodeId = null,
        [property: JsonPropertyName("valid_tax_invoice"), Description("Payments with input VAT: true only if a valid tax invoice was received.")] bool ValidTaxInvoice = true,
        [property: JsonPropertyName("withholding_code_id"), Description("Payments: WHT code when tax is withheld.")] Guid? WithholdingCodeId = null,
        [property: JsonPropertyName("settled_amount"), Description("Lines naming an invoice in another currency than the bank's: how much of the invoice this settles, in the invoice's currency; omit to convert at the day's rate.")] string? SettledAmount = null);

    public sealed record PaymentDocument(
        [property: JsonPropertyName("type"), Description("payment (money out) or receipt (money in).")] string Type,
        [property: JsonPropertyName("bank_account_id"), Description("Bank or cash account from list_bank_accounts.")] Guid BankAccountId,
        [property: JsonPropertyName("date"), Description("yyyy-MM-dd")] DateOnly Date,
        [property: JsonPropertyName("lines")] PaymentLine[] Lines,
        [property: JsonPropertyName("party_id"), Description("Supplier or customer; required to settle invoices or balances.")] Guid? PartyId = null,
        [property: JsonPropertyName("reference")] string? Reference = null,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("exchange_rate"), Description("Bank accounts in another currency: its units per one unit of the ledger's currency; omit for the rate of the date.")] string? ExchangeRate = null);

    public sealed record AdjustmentLine(
        [property: JsonPropertyName("item_id"), Description("An inventory item from list_items (track_inventory true).")] Guid ItemId,
        [property: JsonPropertyName("quantity"), Description("Signed change as a decimal string: negative writes off, positive adds. Give this or counted_quantity.")] string? Quantity = null,
        [property: JsonPropertyName("counted_quantity"), Description("The quantity counted on the date; Books works out the difference from the book quantity.")] string? CountedQuantity = null,
        [property: JsonPropertyName("unit_cost"), Description("Stock coming in only: its cost per unit; omit for the average cost.")] string? UnitCost = null,
        [property: JsonPropertyName("account_id"), Description("The other side; omit for the cost of sales account.")] Guid? AccountId = null,
        [property: JsonPropertyName("description")] string? Description = null);

    public sealed record StatementLine(
        [property: JsonPropertyName("date"), Description("yyyy-MM-dd")] DateOnly Date,
        [property: JsonPropertyName("amount"), Description("Decimal string: positive for money in, negative for money out.")] string Amount,
        [property: JsonPropertyName("description")] string? Description = null,
        [property: JsonPropertyName("reference")] string? Reference = null,
        [property: JsonPropertyName("external_id"), Description("The bank's own transaction ID, if shown; used to skip lines imported before.")] string? ExternalId = null);

    [McpServerTool(Name = "get_fixed_asset_register", ReadOnly = true, Idempotent = true)]
    [Description("The fixed asset register on a date: cost, accumulated depreciation and net book value per asset, checked against the asset accounts.")]
    public Task<CallToolResult> Register(Guid org_id, Guid ledger_id, DateOnly as_of, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await assets.RegisterAsync(await principals.GetAsync(ct), org_id, ledger_id, as_of, ct));

    [McpServerTool(Name = "save_fixed_asset", Idempotent = true)]
    [Description("Adds a fixed asset to the register (straight-line depreciation by month, starting the month after acquisition), or changes one when asset_id "
        + "and expected_version are given. The purchase itself is a purchase invoice or payment line on the asset account. Posting depreciation is for people.")]
    public Task<CallToolResult> SaveAsset(Guid org_id, Guid ledger_id,
        [Description("A new UUID per change.")] Guid idempotency_key,
        string code, string name, DateOnly acquisition_date, [Description("Decimal string.")] string cost, int useful_life_months,
        [Description("Decimal string; default 0.")] string? residual_value = null, Guid? asset_id = null, long? expected_version = null,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var input = FixedAssetEndpoints.Input(new FixedAssetEndpoints.AssetRequest(code, name, acquisition_date, cost, residual_value, useful_life_months));
            var meta = McpWrites.Meta(http, "save_fixed_asset", idempotency_key, new { org_id, ledger_id, code, name, acquisition_date, cost, useful_life_months, residual_value, asset_id })
                with { ExpectedVersion = expected_version };
            return await assets.SaveAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, asset_id, input, ct);
        });

    [McpServerTool(Name = "get_inventory", ReadOnly = true, Idempotent = true)]
    [Description("Inventory items on a date: quantities in and out, on hand, average cost and value, checked against the inventory account.")]
    public Task<CallToolResult> Inventory(Guid org_id, Guid ledger_id, DateOnly as_of, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await inventory.ReportAsync(await principals.GetAsync(ct), org_id, ledger_id, as_of, ct));

    [McpServerTool(Name = "list_stock_adjustments", ReadOnly = true, Idempotent = true)]
    [Description("Stock adjustments and counts with each line's quantity change, book quantity, counted quantity and value.")]
    public Task<CallToolResult> StockAdjustments(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await adjustments.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "save_stock_adjustment_draft", Idempotent = true)]
    [Description("Saves a stock write-off or a stock count as a draft for a person to post. For a count, give counted_quantity per item and Books "
        + "takes the difference from the quantity on hand on the date; leave out items whose count matches. Stock going out is valued at the "
        + "average cost; stock coming in at unit_cost or the average cost. Each item once per adjustment.")]
    public Task<CallToolResult> SaveStockAdjustment(Guid org_id, Guid ledger_id,
        [Description("A new UUID per adjustment; reuse it to retry safely.")] Guid idempotency_key,
        [Description("yyyy-MM-dd")] DateOnly date, AdjustmentLine[] lines, string? reference = null, string? description = null,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var input = StockAdjustmentEndpoints.Input(new StockAdjustmentEndpoints.AdjustmentRequest(date, reference, description,
                lines.Select(l => new StockAdjustmentEndpoints.LineRequest(l.ItemId, l.Description, l.Quantity, l.CountedQuantity, l.UnitCost, l.AccountId)).ToList(), Post: false));
            var meta = McpWrites.Meta(http, "save_stock_adjustment_draft", idempotency_key, new { org_id, ledger_id, date, lines, reference, description });
            return await adjustments.CreateAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, input, post: false, ct);
        });

    [McpServerTool(Name = "list_pay_runs", ReadOnly = true, Idempotent = true)]
    [Description("Pay runs (imported payroll results) with their totals and status.")]
    public Task<CallToolResult> PayRuns(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await payRuns.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "save_pay_run_draft", Idempotent = true)]
    [Description("Saves a month's payroll results from the client's payroll system as a draft pay run (up to 5000 employees) for a person to post. "
        + "Map the payroll file's columns to: employee_code, name, resident, gross, fringe_benefits, salary_tax, fringe_benefit_tax, nssf_employee, "
        + "nssf_employer, other_deductions, net (decimal strings in the ledger's currency); gross must equal net + salary_tax + nssf_employee + "
        + "other_deductions. Keep the payroll system's currency (usually KHR) and give currency; the rate defaults to the 15th. "
        + "Books posts one summary journal and reports the tax for form TOS 01. Omit accounts to reuse the previous pay run's or the template's.")]
    public Task<CallToolResult> SavePayRun(
        Guid org_id, Guid ledger_id,
        [Description("A new UUID per pay run; reuse it to retry safely.")] Guid idempotency_key,
        [Description("The month the pay is for, yyyy-MM.")] string period,
        [Description("The journal date, usually the pay date.")] DateOnly date,
        PayRunEndpoints.LineRequest[] lines,
        PayRunEndpoints.AccountsRequest? accounts = null,
        string? reference = null,
        [Description("The lines' currency, for example KHR; omit for the ledger's.")] string? currency = null,
        [Description("Line currency per one unit of the ledger's currency; omit for the rate of the 15th.")] string? rate = null,
        CancellationToken ct = default) =>
        McpWrites.RunAsync(async () =>
        {
            var input = PayRunEndpoints.Input(new PayRunEndpoints.PayRunRequest(period, date, reference, null, accounts, lines, Post: false, currency, rate));
            var meta = McpWrites.Meta(http, "save_pay_run_draft", idempotency_key, new { org_id, ledger_id, period, date, lines, accounts, reference, currency, rate });
            return await payRuns.CreateAsync(await principals.GetAsync(ct), meta, org_id, ledger_id, input, post: false, ct);
        });

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
                    l.ValidTaxInvoice, l.WithholdingCodeId, l.SettledAmount)).ToList(), Post: false, ExchangeRate: d.ExchangeRate)),
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
