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

public sealed record BankTransferDto(
    Guid Id, Guid LedgerId, long TransferNo, DateOnly Date, Guid FromAccountId, Guid ToAccountId, string Amount,
    string FeeAmount, Guid? FeeAccountId, string? Reference, string? Description, Guid? JournalId, long? JournalNo,
    string Status, IReadOnlyList<JournalIssue> Issues, string Version);

public sealed record BankTransferInput(DateOnly Date, Guid FromAccountId, Guid ToAccountId, decimal Amount, decimal FeeAmount, Guid? FeeAccountId, string? Reference, string? Description);

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
        writes.RunAsync(principal, new WriteRequest(post ? "bank_transfer.save" : "bank_transfer.save_draft", orgId, meta), async scope =>
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
        if (!post)
        {
            transfer.KeptAsDraft(Issues(await posting.CheckAsync(ledger, transfer.ToJournal(), ct)));
            return "draft";
        }
        var outcome = await posting.PostAsync(scope, ledger, BankTransfer.Kind, transfer.Id, transfer.JournalId, transfer.ToJournal(), ct);
        if (outcome.Journal is { } journal)
        {
            transfer.PostedAs(journal.Id);
            return "posted";
        }
        transfer.KeptAsDraft(Issues(outcome.Issues));
        return "not_posted";
    }

    private static string? Issues(IReadOnlyList<JournalIssue> issues) => issues.Count == 0 ? null : BooksJson.Serialize(issues);

    /// <summary>
    /// Changes a transfer. A draft is posted when valid or stays a draft with new reasons. A posted transfer whose
    /// change fails validation is not changed at all, and its posting stays (rule 28); it cannot go back to draft.
    /// </summary>
    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid transferId, BankTransferInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "bank_transfer.save" : "bank_transfer.save_draft", orgId, meta), async scope =>
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
        writes.RunAsync(principal, new WriteRequest("bank_transfer.delete", orgId, meta), async scope =>
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
        transfer.Set(input.Date, input.FromAccountId, input.ToAccountId, input.Amount, input.FeeAmount, input.FeeAccountId, input.Reference, input.Description, isNew);
    }

    private async Task<IReadOnlyList<BankTransferDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct)
    {
        var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        return (await db.BankTransfers.AsNoTracking().Where(t => t.LedgerId == ledgerId && (id == null || t.Id == id))
                .OrderByDescending(t => t.TransferNo)
                .Select(t => new { t, JournalNo = db.Journals.Where(j => j.Id == t.JournalId).Select(j => (long?)j.JournalNo).FirstOrDefault() })
                .ToListAsync(ct))
            .Select(x => new BankTransferDto(x.t.Id, x.t.LedgerId, x.t.TransferNo, x.t.Date, x.t.FromAccountId, x.t.ToAccountId, F(x.t.Amount),
                F(x.t.FeeAmount), x.t.FeeAccountId, x.t.Reference, x.t.Description, x.t.JournalId, x.JournalNo, Text.Of(x.t.Status),
                x.t.Issues is null ? [] : System.Text.Json.JsonSerializer.Deserialize<List<JournalIssue>>(x.t.Issues, BooksJson.Options)!, x.t.Version.ToString()))
            .ToList();
    }
}
