using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

/// <param name="Amount">In the sending account's currency, like the fee.</param>
/// <param name="ToAmount">What arrives, in the receiving account's currency; null when both accounts share a currency.</param>
public sealed record BankTransferDto(
    Guid Id, Guid LedgerId, long TransferNo, DateOnly Date, Guid FromAccountId, Guid ToAccountId, string Amount,
    string FeeAmount, Guid? FeeAccountId, string? Reference, string? Description, Guid? JournalId, long? JournalNo,
    string Status, IReadOnlyList<JournalIssue> Issues, string Version, string? ToAmount = null, string? FromRate = null);

/// <param name="ToAmount">Between accounts in different currencies: what arrives, in the receiving account's currency.</param>
/// <param name="FromRate">When the sending account is in another currency: its units per one unit of the ledger's; omit for the rate of the date.</param>
public sealed record BankTransferInput(DateOnly Date, Guid FromAccountId, Guid ToAccountId, decimal Amount, decimal FeeAmount, Guid? FeeAccountId, string? Reference, string? Description,
    decimal? ToAmount = null, decimal? FromRate = null);

/// <summary>Bank transfers, saved together with their journal through <see cref="DocumentPosting"/>.</summary>
public sealed class BankTransferService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting)
{
    public Task<IReadOnlyList<BankTransferDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    /// <summary>
    /// Saves a transfer. With post = true it is posted when valid; when not, it is kept as a draft with the
    /// reasons (422, committed). With post = false it is kept as a draft on purpose.
    /// </summary>
    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, BankTransferInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "bank_transfer.save" : "bank_transfer.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var transfer = new BankTransfer(Guid.CreateVersion7(), orgId, ledgerId, await posting.NextNumberAsync(ledgerId, BankTransfer.Kind, ct), principal.UserId, scope.Now);
            await SetAsync(transfer, ledger, input, isNew: true, ct);
            db.BankTransfers.Add(transfer);
            var status = await SaveAsync(scope, ledger, transfer, post, ct);
            scope.Audit($"bank_transfer.{status}", "bank_transfer", transfer.Id, new { transfer_no = transfer.TransferNo, journal_id = transfer.JournalId, issues = transfer.Issues });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(status == "not_posted" ? 422 : 201, (await LoadAsync(ledgerId, transfer.Id, ct)).Single(), transfer.Version);
        }, ct);

    /// <summary>Posts or keeps as draft. Returns posted, draft (on purpose) or not_posted (failed, kept as draft with reasons).</summary>
    private async Task<string> SaveAsync(WriteScope scope, Ledger ledger, BankTransfer transfer, bool post, CancellationToken ct)
    {
        var draft = await JournalAsync(ledger, transfer, ct);
        if (!post)
        {
            transfer.KeptAsDraft(Issues(await posting.CheckAsync(ledger, draft, ct)));
            return "draft";
        }
        var outcome = await posting.PostAsync(scope, ledger, BankTransfer.Kind, transfer.Id, transfer.JournalId, draft, ct);
        if (outcome.Journal is { } journal)
        {
            transfer.PostedAs(journal.Id);
            return "posted";
        }
        transfer.KeptAsDraft(Issues(outcome.Issues));
        return "not_posted";
    }

    private static string? Issues(IReadOnlyList<JournalIssue> issues) => issues.Count == 0 ? null : BooksJson.Serialize(issues);

    private async Task<JournalDraft> JournalAsync(Ledger ledger, BankTransfer transfer, CancellationToken ct)
    {
        var currencies = await CurrenciesAsync(ledger, transfer.FromAccountId, transfer.ToAccountId, ct);
        var fromForeign = currencies.From != ledger.BaseCurrency;
        var toForeign = currencies.To != ledger.BaseCurrency;
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        var fx = !toForeign && fromForeign ? await Inventory.AccountAsync(db, ledger.Id, Usage.FxRealized, ct) : (Guid?)null;
        try
        {
            return transfer.ToJournal(minor, fromForeign, toForeign, fx);
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }

    private async Task<(string From, string To)> CurrenciesAsync(Ledger ledger, Guid from, Guid to, CancellationToken ct)
    {
        var banks = await db.BankAccounts.Where(b => b.LedgerId == ledger.Id && (b.AccountId == from || b.AccountId == to)).ToDictionaryAsync(b => b.AccountId, b => b.Currency, ct);
        return (banks.GetValueOrDefault(from, ledger.BaseCurrency), banks.GetValueOrDefault(to, ledger.BaseCurrency));
    }

    /// <summary>
    /// Changes a transfer. A draft is posted when valid or stays a draft with new reasons. A posted transfer whose
    /// change fails validation is not changed at all, and its posting stays (rule 28); it cannot go back to draft.
    /// </summary>
    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid transferId, BankTransferInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "bank_transfer.save" : "bank_transfer.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var transfer = await FindAsync(ledgerId, transferId, ct);
            scope.CheckVersion(transfer.Version);
            var wasPosted = transfer.Status == DocumentStatus.Posted;
            if (wasPosted && !post)
            {
                throw BooksProblem.Conflict("DOCUMENT_POSTED", "A posted document cannot go back to draft; save it or delete it.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var before = (await LoadAsync(ledgerId, transfer.Id, ct)).Single();
            await SetAsync(transfer, ledger, input, isNew: false, ct);
            var status = await SaveAsync(scope, ledger, transfer, post, ct);
            if (wasPosted && status == "not_posted")
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The change cannot be posted; the transfer and its posting are unchanged.")
                {
                    Issues = System.Text.Json.JsonSerializer.Deserialize<List<JournalIssue>>(transfer.Issues!, BooksJson.Options),
                };
            }
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, transfer.Id, ct)).Single();
            scope.Audit($"bank_transfer.{status}", "bank_transfer", transfer.Id, new { before, after });
            return WriteResult.Of(status == "not_posted" ? 422 : 200, after, transfer.Version);
        }, ct);

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid transferId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("bank_transfer.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var transfer = await FindAsync(ledgerId, transferId, ct);
            scope.CheckVersion(transfer.Version);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            if (transfer.Status == DocumentStatus.Posted)
            {
                await posting.DeleteAsync(scope, ledger, transfer.JournalId!.Value, ct);
            }
            transfer.Delete();
            scope.Audit("bank_transfer.deleted", "bank_transfer", transfer.Id, new { transfer_no = transfer.TransferNo });
            return WriteResult.Of(200, new { id = transfer.Id, deleted = true }, transfer.Version);
        }, ct);

    private async Task<BankTransfer> FindAsync(Guid ledgerId, Guid transferId, CancellationToken ct)
    {
        var transfer = await db.BankTransfers.SingleOrDefaultAsync(t => t.LedgerId == ledgerId && t.Id == transferId, ct) ?? throw BooksProblem.NotFound();
        return transfer.Status == DocumentStatus.Deleted ? throw BooksProblem.Conflict("DOCUMENT_DELETED", "This transfer was deleted.") : transfer;
    }

    /// <summary>Both sides must be bank accounts of this ledger; a fee goes to a Cost &amp; Expense account.</summary>
    private async Task SetAsync(BankTransfer transfer, Ledger ledger, BankTransferInput input, bool isNew, CancellationToken ct)
    {
        var banks = await db.BankAccounts.Where(b => b.LedgerId == ledger.Id && (b.AccountId == input.FromAccountId || b.AccountId == input.ToAccountId))
            .Select(b => b.AccountId).ToListAsync(ct);
        if (!banks.Contains(input.FromAccountId) || !banks.Contains(input.ToAccountId))
        {
            throw BooksProblem.Invalid("from_account_id and to_account_id must be bank accounts of this ledger.");
        }
        if (input.FeeAccountId is { } fee
            && !await db.Accounts.AnyAsync(a => a.LedgerId == ledger.Id && a.Id == fee && a.Type == AccountType.CostAndExpense, ct))
        {
            throw BooksProblem.Invalid("fee_account_id must be a Cost & Expense account of this ledger.");
        }
        // Between currencies the receiving amount is given; the sending side converts at its rate (M12.2).
        var (fromCurrency, toCurrency) = await CurrenciesAsync(ledger, input.FromAccountId, input.ToAccountId, ct);
        if ((fromCurrency != toCurrency) != (input.ToAmount is not null))
        {
            throw BooksProblem.Invalid(fromCurrency != toCurrency
                ? $"A transfer from {fromCurrency} to {toCurrency} needs to_amount, what arrives in {toCurrency}."
                : "to_amount applies only between accounts in different currencies.");
        }
        var fromRate = await Rates.ForDocumentAsync(db, ledger, fromCurrency, input.FromRate, input.Date, ct);
        try
        {
            transfer.Set(input.Date, input.FromAccountId, input.ToAccountId, input.Amount, input.FeeAmount, input.FeeAccountId, input.Reference, input.Description, isNew,
                input.ToAmount, fromRate);
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }

    private async Task<IReadOnlyList<BankTransferDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct)
    {
        var baseCurrency = await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct);
        var bankMinor = await db.BankAccounts.Where(b => b.LedgerId == ledgerId).ToDictionaryAsync(b => b.AccountId, b => Currencies.Require(b.Currency).MinorUnits, ct);
        int M(Guid account) => bankMinor.TryGetValue(account, out var m) ? m : Currencies.Require(baseCurrency).MinorUnits;
        static string F(decimal v, int minor) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        return (await db.BankTransfers.AsNoTracking().Where(t => t.LedgerId == ledgerId && (id == null || t.Id == id))
                .OrderByDescending(t => t.TransferNo)
                .Select(t => new { t, JournalNo = db.Journals.Where(j => j.Id == t.JournalId).Select(j => (long?)j.JournalNo).FirstOrDefault() })
                .ToListAsync(ct))
            .Select(x => new BankTransferDto(x.t.Id, x.t.LedgerId, x.t.TransferNo, x.t.Date, x.t.FromAccountId, x.t.ToAccountId, F(x.t.Amount, M(x.t.FromAccountId)),
                F(x.t.FeeAmount, M(x.t.FromAccountId)), x.t.FeeAccountId, x.t.Reference, x.t.Description, x.t.JournalId, x.JournalNo, Text.Of(x.t.Status),
                x.t.Issues is null ? [] : System.Text.Json.JsonSerializer.Deserialize<List<JournalIssue>>(x.t.Issues, BooksJson.Options)!, x.t.Version.ToString(),
                x.t.ToAmount is { } to ? F(to, M(x.t.ToAccountId)) : null, x.t.FromRate?.ToString("0.##########", CultureInfo.InvariantCulture)))
            .ToList();
    }
}
