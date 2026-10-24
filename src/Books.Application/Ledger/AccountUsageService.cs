using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

/// <summary>
/// Account Usage: which business purposes an account serves. Subsystems (tax codes, bank accounts, documents)
/// may only reference accounts carrying the matching usage, so all account mappings follow one rule.
/// Every change runs inside the caller's write transaction, together with its checks.
/// </summary>
public sealed class AccountUsageService(IBooksDb db, WriteRunner writes)
{
    /// <summary>
    /// Adds a usage if the account does not carry it yet. Payable and Receivable need a control account: an
    /// unused account is marked control in the same transaction; a used one is refused.
    /// </summary>
    internal async Task EnsureAsync(Account account, Usage usage, CancellationToken ct)
    {
        if (await db.AccountUsages.AnyAsync(u => u.AccountId == account.Id && u.Usage == usage, ct)
            || db.AccountUsages.Local.Any(u => u.AccountId == account.Id && u.Usage == usage))
        {
            return;
        }
        if (Usages.RequiresControl(usage) && !account.IsControl)
        {
            if (await db.JournalLines.AnyAsync(l => l.AccountId == account.Id, ct))
            {
                throw BooksProblem.Invalid($"The {usage} usage needs a control account, and this account already has journal lines.");
            }
            account.MarkAsControl();
        }
        db.AccountUsages.Add(new AccountUsage(account, usage));
    }

    /// <summary>Replaces an account's usages. A usage cannot be removed while something still relies on it.</summary>
    public Task<WriteResult> ReplaceAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId, IReadOnlyList<Usage> usages, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("account.usages", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == accountId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(account.Version);
            var current = await db.AccountUsages.Where(u => u.AccountId == accountId).ToListAsync(ct);
            var before = current.Select(u => Text.Of(u.Usage)).Order().ToList();
            foreach (var removed in current.Where(u => !usages.Contains(u.Usage)))
            {
                await EnsureRemovableAsync(account, removed.Usage, ct);
                db.AccountUsages.Remove(removed);
            }
            foreach (var usage in usages.Distinct().Where(u => current.All(c => c.Usage != u)))
            {
                await EnsureAsync(account, usage, ct);
            }
            account.Touch();
            var after = usages.Distinct().Select(Text.Of).Order().ToList();
            scope.Audit("account.usages_changed", "account", account.Id, new { before, after });
            return WriteResult.Of(200, new { account_id = account.Id, usages = after, is_control = account.IsControl }, account.Version);
        }, ct);

    /// <summary>Owner decision: a usage stays while bank details, tax codes or open documents rely on it.</summary>
    private async Task EnsureRemovableAsync(Account account, Usage usage, CancellationToken ct)
    {
        var inUse = usage switch
        {
            Usage.Bank => await db.BankAccounts.AnyAsync(b => b.AccountId == account.Id, ct),
            Usage.OutputVat or Usage.InputVat or Usage.WithholdingPayable =>
                await db.TaxCodes.AnyAsync(t => t.AccountId == account.Id, ct),
            Usage.NonDeductibleVat => await db.TaxCodes.AnyAsync(t => t.NonDeductibleAccountId == account.Id, ct),
            // Until payments exist every posted invoice is open; M5.4 narrows this to unpaid invoices.
            Usage.Payable => await db.PurchaseInvoices.AnyAsync(i => i.PayableAccountId == account.Id && i.Status == Books.Domain.Documents.DocumentStatus.Posted, ct),
            Usage.Receivable => await db.SalesInvoices.AnyAsync(i => i.ReceivableAccountId == account.Id && i.Status == Books.Domain.Documents.DocumentStatus.Posted, ct),
            _ => false,
        };
        if (inUse)
        {
            throw BooksProblem.Conflict("USAGE_IN_USE", $"The {Text.Of(usage)} usage is still relied on by this account's bank details, tax codes or open documents.");
        }
    }

    public static Usage Parse(string? usage) => usage switch
    {
        "payable" => Usage.Payable,
        "receivable" => Usage.Receivable,
        "bank" => Usage.Bank,
        "output_vat" => Usage.OutputVat,
        "input_vat" => Usage.InputVat,
        "non_deductible_vat" => Usage.NonDeductibleVat,
        "withholding_payable" => Usage.WithholdingPayable,
        _ => throw BooksProblem.Invalid("usage must be one of payable, receivable, bank, output_vat, input_vat, non_deductible_vat, withholding_payable."),
    };
}
