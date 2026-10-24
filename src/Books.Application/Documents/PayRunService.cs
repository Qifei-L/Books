using System.Globalization;
using System.Text.Json;
using Books.Application.Access;
using Books.Application.Common;
using Books.Application.Journals;
using Books.Domain.Access;
using Books.Domain.Documents;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Documents;

public sealed record PayRunLineInput(
    string EmployeeCode, string Name, bool Resident, decimal Gross, decimal FringeBenefits, decimal SalaryTax, decimal FringeBenefitTax,
    decimal NssfEmployee, decimal NssfEmployer, decimal OtherDeductions, decimal Net);

/// <param name="Accounts">Omit to use the previous pay run's accounts, else the accounts with the payroll usages.</param>
/// <param name="Currency">The lines' currency; omit for the ledger's. Payroll systems in Cambodia usually report KHR.</param>
/// <param name="Rate">Line currency per one unit of the ledger's currency; omit to use the rate of the 15th.</param>
public sealed record PayRunInput(
    DateOnly Period, DateOnly Date, string? Reference, string? Description, PayRunAccounts? Accounts, IReadOnlyList<PayRunLineInput> Lines,
    string? Currency = null, decimal? Rate = null);

public sealed record PayRunTotals(int Employees, string Gross, string FringeBenefits, string SalaryTax, string FringeBenefitTax, string NssfEmployee, string NssfEmployer, string OtherDeductions, string Net);

/// <param name="Totals">In the lines' currency.</param>
public sealed record PayRunDto(
    Guid Id, Guid LedgerId, long PayRunNo, string Period, DateOnly Date, string? Reference, string? Description, PayRunAccounts Accounts, string Currency, string? Rate,
    PayRunTotals Totals, string Status, IReadOnlyList<JournalIssue> Issues, Guid? JournalId, long? JournalNo, string Version);

/// <summary>
/// Payroll results imported from the payroll system (owner decision: Books receives results; it does not calculate
/// pay). One summary journal per pay run; employee lines feed form TOS 01. AI clients save drafts; people post.
/// </summary>
public sealed class PayRunService(IBooksDb db, WriteRunner writes, ReadRunner reads, DocumentPosting posting)
{
    public Task<IReadOnlyList<PayRunDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<PayRunDto>>(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, PayRunInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "pay_run.save" : "pay_run.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var run = new PayRun(Guid.CreateVersion7(), orgId, ledgerId, await posting.NextNumberAsync(ledgerId, PayRun.Kind, ct), principal.UserId, scope.Now);
            await SetAsync(run, ledger, input, isNew: true, ct);
            db.PayRuns.Add(run);
            var status = await SaveAsync(scope, ledger, run, post, ct);
            scope.Audit($"pay_run.{status}", "pay_run", run.Id, new { pay_run_no = run.PayRunNo, period = input.Period, employees = run.Lines.Count, journal_id = run.JournalId });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(status == "not_posted" ? 422 : 201, (await LoadAsync(ledgerId, run.Id, ct)).Single(), run.Version);
        }, ct);

    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid payRunId, PayRunInput input, bool post, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest(post ? "pay_run.save" : "pay_run.save_draft", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var run = await FindAsync(ledgerId, payRunId, ct);
            scope.CheckVersion(run.Version);
            var wasPosted = run.Status == DocumentStatus.Posted;
            if (wasPosted && !post)
            {
                throw BooksProblem.Conflict("DOCUMENT_POSTED", "A posted document cannot go back to draft; save it or delete it.");
            }
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            db.PayRunLines.RemoveRange(run.Lines);
            await SetAsync(run, ledger, input, isNew: false, ct);
            var status = await SaveAsync(scope, ledger, run, post, ct);
            if (wasPosted && status == "not_posted")
            {
                throw new BooksProblem(422, "DOCUMENT_INVALID", "The change cannot be posted; the pay run and its posting are unchanged.")
                {
                    Issues = JsonSerializer.Deserialize<List<JournalIssue>>(run.Issues!, BooksJson.Options),
                };
            }
            scope.Audit($"pay_run.{status}", "pay_run", run.Id, new { pay_run_no = run.PayRunNo, employees = run.Lines.Count, journal_id = run.JournalId });
            await db.SaveChangesAsync(ct);
            return WriteResult.Of(status == "not_posted" ? 422 : 200, (await LoadAsync(ledgerId, run.Id, ct)).Single(), run.Version);
        }, ct);

    public Task<WriteResult> DeleteAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid payRunId, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("pay_run.delete", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var run = await FindAsync(ledgerId, payRunId, ct);
            scope.CheckVersion(run.Version);
            if (run.Status == DocumentStatus.Posted)
            {
                await posting.DeleteAsync(scope, await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct), run.JournalId!.Value, ct);
            }
            run.Delete();
            scope.Audit("pay_run.deleted", "pay_run", run.Id, new { pay_run_no = run.PayRunNo });
            return WriteResult.Of(200, new { id = run.Id, deleted = true }, run.Version);
        }, ct);

    private async Task SetAsync(PayRun run, Ledger ledger, PayRunInput input, bool isNew, CancellationToken ct)
    {
        var accounts = input.Accounts
            ?? await db.PayRuns.AsNoTracking().Where(r => r.LedgerId == ledger.Id && r.Status != DocumentStatus.Deleted && r.Id != run.Id)
                .OrderByDescending(r => r.Period).ThenByDescending(r => r.PayRunNo).Select(r => r.Accounts).FirstOrDefaultAsync(ct)
            ?? await FromUsagesAsync(ledger.Id, ct)
            ?? throw BooksProblem.Invalid("Give accounts, or apply the Cambodia template, which sets up the payroll accounts.");
        var currency = input.Currency ?? ledger.BaseCurrency;
        var rate = input.Rate;
        if (currency != ledger.BaseCurrency && rate is null)
        {
            var fifteenth = input.Period.AddDays(14);
            rate = await db.ExchangeRates.AsNoTracking()
                .Where(r => r.OrgId == ledger.OrgId && r.FromCurrency == ledger.BaseCurrency && r.ToCurrency == currency && r.Date <= fifteenth)
                .OrderByDescending(r => r.Date).Select(r => (decimal?)r.Rate).FirstOrDefaultAsync(ct)
                ?? throw BooksProblem.Invalid($"No {ledger.BaseCurrency}→{currency} rate on or before {fifteenth:yyyy-MM-dd}; give rate.");
        }
        var ids = new[] { accounts.SalaryExpense, accounts.NssfExpense, accounts.SalariesPayable, accounts.SalaryTaxPayable, accounts.NssfPayable }
            .Concat(accounts.OtherDeductions is { } other ? [other] : []).Distinct().ToList();
        var found = await db.Accounts.AsNoTracking().Where(a => a.LedgerId == ledger.Id && ids.Contains(a.Id)).ToListAsync(ct);
        if (found.Count != ids.Count)
        {
            throw BooksProblem.Invalid("Every pay run account must be an account of this ledger.");
        }
        if (found.FirstOrDefault(a => a.IsControl) is { } control)
        {
            throw BooksProblem.Invalid($"{control.Code} is a control account; pay runs post to ordinary accounts.");
        }
        try
        {
            run.Set(input.Period, input.Date, input.Reference, input.Description, accounts,
                (input.Lines ?? throw BooksProblem.Invalid("lines is required.")).Select(l => new PayRunLine(l.EmployeeCode, l.Name, l.Resident, l.Gross, l.FringeBenefits,
                    l.SalaryTax, l.FringeBenefitTax, l.NssfEmployee, l.NssfEmployer, l.OtherDeductions, l.Net)), isNew, currency, rate, ledger.BaseCurrency);
        }
        catch (DomainException e)
        {
            throw BooksProblem.Invalid(e.Message);
        }
    }

    /// <summary>The accounts carrying the payroll usages, when each usage has exactly one.</summary>
    private async Task<PayRunAccounts?> FromUsagesAsync(Guid ledgerId, CancellationToken ct)
    {
        var payroll = new[] { Usage.SalaryExpense, Usage.NssfExpense, Usage.SalariesPayable, Usage.SalaryTaxPayable, Usage.NssfPayable };
        var found = (await db.AccountUsages.AsNoTracking().Where(u => u.LedgerId == ledgerId && payroll.Contains(u.Usage)).ToListAsync(ct))
            .GroupBy(u => u.Usage).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().AccountId);
        return payroll.All(found.ContainsKey)
            ? new PayRunAccounts(found[Usage.SalaryExpense], found[Usage.NssfExpense], found[Usage.SalariesPayable], found[Usage.SalaryTaxPayable], found[Usage.NssfPayable], null)
            : null;
    }

    private async Task<string> SaveAsync(WriteScope scope, Ledger ledger, PayRun run, bool post, CancellationToken ct)
    {
        var minor = Currencies.Require(ledger.BaseCurrency).MinorUnits;
        if (post)
        {
            JournalService.RefuseAgentPosting(scope.Principal, touchesPosting: true);
        }
        if (!post)
        {
            var issues = await posting.CheckAsync(ledger, run.ToJournal(minor), ct);
            run.KeptAsDraft(issues.Count == 0 ? null : BooksJson.Serialize(issues));
            return "draft";
        }
        var outcome = await posting.PostAsync(scope, ledger, PayRun.Kind, run.Id, run.JournalId, run.ToJournal(minor), ct);
        if (outcome.Journal is { } journal)
        {
            run.PostedAs(journal.Id);
            return "posted";
        }
        run.KeptAsDraft(BooksJson.Serialize(outcome.Issues));
        return "not_posted";
    }

    private async Task<PayRun> FindAsync(Guid ledgerId, Guid payRunId, CancellationToken ct)
    {
        var run = await db.PayRuns.Include(r => r.Lines).SingleOrDefaultAsync(r => r.LedgerId == ledgerId && r.Id == payRunId, ct) ?? throw BooksProblem.NotFound();
        return run.Status == DocumentStatus.Deleted ? throw BooksProblem.Conflict("DOCUMENT_DELETED", "This pay run was deleted.") : run;
    }

    private async Task<IReadOnlyList<PayRunDto>> LoadAsync(Guid ledgerId, Guid? id, CancellationToken ct)
    {
        var minor = Currencies.Require(await db.Ledgers.Where(l => l.Id == ledgerId).Select(l => l.BaseCurrency).SingleAsync(ct)).MinorUnits;
        string F(decimal v) => v.ToString("F" + minor, CultureInfo.InvariantCulture);
        var rows = await db.PayRuns.AsNoTracking().Include(r => r.Lines)
            .Where(r => r.LedgerId == ledgerId && (id == null || r.Id == id))
            .OrderByDescending(r => r.Period).ThenByDescending(r => r.PayRunNo)
            .Select(r => new { r, JournalNo = db.Journals.Where(j => j.Id == r.JournalId).Select(j => (long?)j.JournalNo).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(x =>
        {
            var l = x.r.Lines;
            var totals = new PayRunTotals(l.Count, F(l.Sum(i => i.Gross)), F(l.Sum(i => i.FringeBenefits)), F(l.Sum(i => i.SalaryTax)), F(l.Sum(i => i.FringeBenefitTax)),
                F(l.Sum(i => i.NssfEmployee)), F(l.Sum(i => i.NssfEmployer)), F(l.Sum(i => i.OtherDeductions)), F(l.Sum(i => i.Net)));
            var lm = Currencies.Require(x.r.Currency).MinorUnits;
            string L(decimal v) => v.ToString("F" + lm, CultureInfo.InvariantCulture);
            totals = new PayRunTotals(l.Count, L(l.Sum(i => i.Gross)), L(l.Sum(i => i.FringeBenefits)), L(l.Sum(i => i.SalaryTax)), L(l.Sum(i => i.FringeBenefitTax)),
                L(l.Sum(i => i.NssfEmployee)), L(l.Sum(i => i.NssfEmployer)), L(l.Sum(i => i.OtherDeductions)), L(l.Sum(i => i.Net)));
            return new PayRunDto(x.r.Id, x.r.LedgerId, x.r.PayRunNo, x.r.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture), x.r.Date, x.r.Reference, x.r.Description,
                x.r.Accounts, x.r.Currency, x.r.Rate?.ToString("0.##########", CultureInfo.InvariantCulture), totals, Text.Of(x.r.Status), x.r.Issues is null ? [] : JsonSerializer.Deserialize<List<JournalIssue>>(x.r.Issues, BooksJson.Options)!,
                x.r.JournalId, x.JournalNo, x.r.Version.ToString(CultureInfo.InvariantCulture));
        }).ToList();
    }
}
