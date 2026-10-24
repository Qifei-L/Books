using System.Globalization;
using Books.Application.Access;
using Books.Application.Common;
using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Ledgers;

public sealed record BankAccountDto(Guid AccountId, Guid LedgerId, string Code, string Name, string BankName, string? AccountNumber, string Currency, string Version);

public sealed record ExchangeRateDto(DateOnly Date, string FromCurrency, string ToCurrency, string Rate, string Source, string Version);

/// <summary>Bank accounts of a ledger (edit access) and the organization's exchange rates (administrators).</summary>
public sealed class BankService(IBooksDb db, WriteRunner writes, ReadRunner reads, AccountUsageService usages)
{
    public Task<IReadOnlyList<BankAccountDto>> ListAsync(Principal principal, Guid orgId, Guid ledgerId, CancellationToken ct) =>
        reads.RunAsync(principal, orgId, async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Read, ct);
            return await LoadAsync(ledgerId, null, ct);
        }, ct);

    public Task<WriteResult> CreateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId, string bankName, string? accountNumber, CancellationToken ct,
        string? currency = null) =>
        writes.RunAsync(principal, new WriteRequest("bank_account.create", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var ledger = await db.Ledgers.SingleAsync(l => l.Id == ledgerId, ct);
            var account = await db.Accounts.SingleOrDefaultAsync(a => a.LedgerId == ledgerId && a.Id == accountId, ct)
                ?? throw BooksProblem.Invalid("account_id must be an account in this ledger.");
            if (await db.BankAccounts.AnyAsync(b => b.AccountId == accountId, ct))
            {
                throw BooksProblem.Conflict("BANK_ACCOUNT_EXISTS", "This account already has bank details.");
            }
            if (currency is not null && Currencies.Require(currency).Code != ledger.BaseCurrency
                && await db.JournalLines.AnyAsync(l => l.LedgerId == ledgerId && l.AccountId == accountId, ct))
            {
                throw BooksProblem.Conflict("ACCOUNT_IN_USE", "An account with postings in the ledger's currency cannot become a foreign-currency bank account.");
            }
            var bank = new BankAccount(ledger, account, bankName, accountNumber, scope.Now, currency);
            await usages.EnsureAsync(account, Usage.Bank, ct);
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync(ct);
            var dto = (await LoadAsync(ledgerId, accountId, ct)).Single();
            scope.Audit("bank_account.created", "account", accountId, dto);
            return WriteResult.Of(201, dto, bank.Version);
        }, ct);

    public Task<WriteResult> UpdateAsync(Principal principal, WriteMeta meta, Guid orgId, Guid ledgerId, Guid accountId, string bankName, string? accountNumber, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("bank_account.update", orgId, meta, LedgerId: ledgerId), async scope =>
        {
            await scope.RequireLedgerAsync(ledgerId, LedgerAccess.Edit, ct);
            var bank = await db.BankAccounts.SingleOrDefaultAsync(b => b.LedgerId == ledgerId && b.AccountId == accountId, ct) ?? throw BooksProblem.NotFound();
            scope.CheckVersion(bank.Version);
            var before = (await LoadAsync(ledgerId, accountId, ct)).Single();
            bank.Update(bankName, accountNumber);
            await db.SaveChangesAsync(ct);
            var after = (await LoadAsync(ledgerId, accountId, ct)).Single();
            scope.Audit("bank_account.updated", "account", accountId, new { before, after });
            return WriteResult.Of(200, after, bank.Version);
        }, ct);

    /// <summary>Rates between two dates, any active member can read.</summary>
    public Task<IReadOnlyList<ExchangeRateDto>> RatesAsync(Principal principal, Guid orgId, DateOnly from, DateOnly to, CancellationToken ct) =>
        reads.RunAsync<IReadOnlyList<ExchangeRateDto>>(principal, orgId, async scope =>
        {
            scope.RequireMember();
            return (await db.ExchangeRates.AsNoTracking().Where(r => r.OrgId == orgId && r.Date >= from && r.Date <= to)
                .OrderBy(r => r.Date).ThenBy(r => r.FromCurrency).ToListAsync(ct)).Select(ToDto).ToList();
        }, ct);

    /// <summary>Sets the rate for a date and currency pair, replacing an earlier entry (administrators).</summary>
    public Task<WriteResult> SetRateAsync(Principal principal, WriteMeta meta, Guid orgId, DateOnly date, string from, string to, decimal rate, string source, CancellationToken ct) =>
        writes.RunAsync(principal, new WriteRequest("exchange_rate.set", orgId, meta), async scope =>
        {
            scope.RequireAdmin();
            var candidate = new ExchangeRate(orgId, date, from, to, rate, source, scope.Now);
            var existing = await db.ExchangeRates.SingleOrDefaultAsync(
                r => r.OrgId == orgId && r.Date == date && r.FromCurrency == candidate.FromCurrency && r.ToCurrency == candidate.ToCurrency, ct);
            var before = existing is null ? null : ToDto(existing);
            if (existing is null)
            {
                db.ExchangeRates.Add(candidate);
            }
            else
            {
                existing.Replace(rate, source);
            }
            var saved = existing ?? candidate;
            scope.Audit("exchange_rate.set", "organization", orgId, new { before, after = ToDto(saved) });
            return WriteResult.Of(200, ToDto(saved), saved.Version);
        }, ct);

    private static ExchangeRateDto ToDto(ExchangeRate r) =>
        new(r.Date, r.FromCurrency, r.ToCurrency, r.Rate.ToString("0.##########", CultureInfo.InvariantCulture), r.Source, r.Version.ToString());

    private async Task<IReadOnlyList<BankAccountDto>> LoadAsync(Guid ledgerId, Guid? accountId, CancellationToken ct) =>
        (await db.BankAccounts.AsNoTracking().Where(b => b.LedgerId == ledgerId && (accountId == null || b.AccountId == accountId))
            .Join(db.Accounts, b => b.AccountId, a => a.Id, (b, a) => new { b, a.Code, a.Name })
            .OrderBy(x => x.Code).ToListAsync(ct))
        .Select(x => new BankAccountDto(x.b.AccountId, x.b.LedgerId, x.Code, x.Name, x.b.BankName, x.b.AccountNumber, x.b.Currency, x.b.Version.ToString()))
        .ToList();
}
