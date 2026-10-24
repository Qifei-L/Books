using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Books.Api.Auth;
using Books.Api.Http;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Application.Reporting;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Books.Api.Mcp;

/// <summary>
/// Journal and report tools. They call the same application services as the HTTP API, so validation, access,
/// idempotency and versioning are identical. Writes require an idempotency key; retrying with it never posts twice.
/// </summary>
[McpServerToolType]
public sealed class BooksJournalTools(PrincipalAccessor principals, JournalService journals, ReportService reports, StatementService statements, IHttpContextAccessor http)
{
    public sealed record JournalLineInput(
        [property: JsonPropertyName("account_id"), Description("Account ID from list_accounts; must be postable.")] Guid AccountId,
        [property: JsonPropertyName("debit"), Description("Decimal string such as \"100.00\", or omit.")] string? Debit,
        [property: JsonPropertyName("credit"), Description("Decimal string such as \"100.00\", or omit.")] string? Credit,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("party_id"), Description("Optional customer or supplier ID.")] Guid? PartyId = null,
        [property: JsonPropertyName("currency_amount"), Description("Lines on a bank account in another currency (for example KHR): the amount in that currency.")] string? CurrencyAmount = null);

    [McpServerTool(Name = "list_journals", ReadOnly = true, Idempotent = true)]
    [Description("Recent journals of a ledger: posted ones, plus your own drafts and failed saves.")]
    public Task<CallToolResult> ListJournals(Guid org_id, Guid ledger_id, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await journals.ListAsync(await principals.GetAsync(ct), org_id, ledger_id, ct));

    [McpServerTool(Name = "save_journal", Idempotent = true)]
    [Description("Saves a draft journal, or replaces one of your drafts when journal_id and expected_version are given. "
        + "AI clients cannot post: send post=false; a person reviews and posts the draft. The result lists any issues that would block posting. "
        + "Use the Suspense account for amounts whose account is not known yet. "
        + "Always send a new UUID as idempotency_key, and reuse it only to retry the same request.")]
    public Task<CallToolResult> SaveJournal(
        Guid org_id,
        Guid ledger_id,
        [Description("A new UUID per intended change; reuse it to retry safely.")] Guid idempotency_key,
        [Description("Accounting date, yyyy-MM-dd.")] DateOnly accounting_date,
        [Description("Each line has a debit or a credit. Debits must equal credits to post.")] JournalLineInput[] lines,
        bool post,
        [Description("Your own reference, for example a voucher number. Free text.")] string? reference = null,
        string? description = null,
        [Description("Set to edit an existing journal.")] Guid? journal_id = null,
        [Description("The journal's current version; required when journal_id is set.")] long? expected_version = null,
        CancellationToken ct = default) =>
        WriteAsync(async () =>
        {
            var request = new JournalEndpoints.JournalRequest(accounting_date,
                lines.Select(l => new JournalEndpoints.LineRequest(l.AccountId, l.Debit, l.Credit, l.Description, l.PartyId, CurrencyAmount: l.CurrencyAmount)).ToList(), post, reference, description);
            var draft = JournalEndpoints.ToDraft(request);
            var meta = Meta("save_journal", idempotency_key, expected_version, new { org_id, ledger_id, journal_id, request });
            var principal = await principals.GetAsync(ct);
            return journal_id is { } id
                ? await journals.UpdateAsync(principal, meta, org_id, ledger_id, id, draft, post, ct)
                : await journals.CreateAsync(principal, meta, org_id, ledger_id, draft, post, ct);
        });

    [McpServerTool(Name = "delete_journal", Destructive = true, Idempotent = true)]
    [Description("Deletes one of your draft journals. Posted journals can only be deleted by a person.")]
    public Task<CallToolResult> DeleteJournal(Guid org_id, Guid ledger_id, Guid journal_id, Guid idempotency_key, long expected_version, CancellationToken ct = default) =>
        WriteAsync(async () => await journals.DeleteAsync(
            await principals.GetAsync(ct), Meta("delete_journal", idempotency_key, expected_version, new { org_id, ledger_id, journal_id }),
            org_id, ledger_id, journal_id, ct));

    [McpServerTool(Name = "get_trial_balance", ReadOnly = true, Idempotent = true)]
    [Description("Trial Balance for a date range: opening, period and closing debit/credit per account, totals and whether it balances.")]
    public Task<CallToolResult> GetTrialBalance(Guid org_id, Guid ledger_id, DateOnly from, DateOnly to, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await reports.TrialBalanceAsync(await principals.GetAsync(ct), org_id, ledger_id, from, to, ct));

    [McpServerTool(Name = "get_profit_and_loss", ReadOnly = true, Idempotent = true)]
    [Description("Profit and Loss for a date range: Income and Cost & Expense with group subtotals, and net profit. The range may span fiscal years.")]
    public Task<CallToolResult> GetProfitAndLoss(Guid org_id, Guid ledger_id, DateOnly from, DateOnly to, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await statements.ProfitAndLossAsync(await principals.GetAsync(ct), org_id, ledger_id, from, to, ct));

    [McpServerTool(Name = "get_balance_sheet", ReadOnly = true, Idempotent = true)]
    [Description("Balance Sheet as of a date: Assets, Liabilities and Equity with group subtotals. Prior years' and current year profit are calculated lines.")]
    public Task<CallToolResult> GetBalanceSheet(Guid org_id, Guid ledger_id, DateOnly as_of, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await statements.BalanceSheetAsync(await principals.GetAsync(ct), org_id, ledger_id, as_of, ct));

    [McpServerTool(Name = "get_gl_detail", ReadOnly = true, Idempotent = true)]
    [Description("GL Detail for one account: opening balance, each posting with running balance, period totals.")]
    public Task<CallToolResult> GetGlDetail(Guid org_id, Guid ledger_id, Guid account_id, DateOnly from, DateOnly to, CancellationToken ct) =>
        BooksReadTools.RunAsync(async () => await reports.GlDetailAsync(await principals.GetAsync(ct), org_id, ledger_id, account_id, from, to, ct));

    /// <summary>The fingerprint covers the tool and its arguments, so the same key with different content is rejected.</summary>
    private WriteMeta Meta(string tool, Guid key, long? expectedVersion, object arguments)
    {
        var fingerprint = JsonSerializer.Serialize(new { tool, arguments, expectedVersion }, BooksJson.Options);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
        return new WriteMeta(key, hash, expectedVersion, http.HttpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"));
    }

    /// <summary>A committed write returns {"status", "data"}; a 4xx outcome (including a saved Error revision) is an error result.</summary>
    private static async Task<CallToolResult> WriteAsync(Func<Task<WriteResult>> write)
    {
        try
        {
            var result = await write();
            var text = $"{{\"status\":{result.Status},\"data\":{result.Json}}}";
            return new CallToolResult { IsError = result.Status >= 400, Content = [new TextContentBlock { Text = text }] };
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
