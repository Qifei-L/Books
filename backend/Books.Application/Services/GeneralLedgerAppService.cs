using Books.Application.DTOs;
using Books.Application.Interfaces;
using Books.Domain.Entities;
using Books.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Services;

public class GeneralLedgerAppService(
    IAppDbContext db,
    JournalService journalService,
    ReportService reportService)
{
    public async Task<Ledger> GetDefaultLedgerAsync()
    {
        var ledger = await db.Ledgers.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (ledger is not null)
        {
            return ledger;
        }

        ledger = new Ledger { Name = "Demo Ledger", IsActive = true };
        db.Ledgers.Add(ledger);
        await db.SaveChangesAsync();
        return ledger;
    }

    public async Task<DashboardDto> GetDashboardAsync()
    {
        var ledger = await GetDefaultLedgerAsync();
        var accountsCount = await db.Accounts.CountAsync(x => x.LedgerId == ledger.Id);
        var draftCount = await db.JournalEntries.CountAsync(x => x.LedgerId == ledger.Id && x.Status == JournalStatus.Draft);
        var postedCount = await db.JournalEntries.CountAsync(x => x.LedgerId == ledger.Id && x.Status == JournalStatus.Posted);
        var latestEntries = await db.JournalEntries
            .Where(x => x.LedgerId == ledger.Id)
            .OrderByDescending(x => x.EntryDate)
            .ThenByDescending(x => x.Id)
            .Take(8)
            .Select(x => new JournalEntryListDto(x.Id, x.JournalNo, x.EntryDate, x.Description, x.Status, x.Lines.Sum(l => l.Debit), x.Lines.Sum(l => l.Credit)))
            .ToListAsync();

        return new DashboardDto(ledger.Id, ledger.Name, "2026-06", "USD", accountsCount, draftCount, postedCount, latestEntries);
    }

    public async Task<List<Account>> GetAccountsAsync(string? search, AccountType? type, bool? isActive)
    {
        var ledger = await GetDefaultLedgerAsync();
        var query = db.Accounts.Where(x => x.LedgerId == ledger.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(term) || x.Name.ToLower().Contains(term));
        }

        if (type.HasValue)
        {
            query = query.Where(x => x.Type == type.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        return await query.OrderBy(x => x.Code).ToListAsync();
    }

    public async Task SaveAccountAsync(AccountEditDto account)
    {
        var ledger = await GetDefaultLedgerAsync();
        var entity = account.Id.HasValue
            ? await db.Accounts.FirstAsync(x => x.Id == account.Id.Value)
            : new Account { LedgerId = ledger.Id };

        entity.Code = account.Code.Trim();
        entity.Name = account.Name.Trim();
        entity.Type = account.Type;
        entity.Description = account.Description?.Trim() ?? string.Empty;
        entity.IsActive = account.IsActive;

        if (!account.Id.HasValue)
        {
            db.Accounts.Add(entity);
        }

        await db.SaveChangesAsync();
    }

    public async Task ToggleAccountAsync(int id)
    {
        var account = await db.Accounts.FirstAsync(x => x.Id == id);
        account.IsActive = !account.IsActive;
        await db.SaveChangesAsync();
    }

    public async Task<List<JournalEntryListDto>> GetJournalEntriesAsync(JournalStatus? status = null)
    {
        var ledger = await GetDefaultLedgerAsync();
        var query = db.JournalEntries.Where(x => x.LedgerId == ledger.Id);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        return await query
            .OrderByDescending(x => x.EntryDate)
            .ThenByDescending(x => x.Id)
            .Select(x => new JournalEntryListDto(x.Id, x.JournalNo, x.EntryDate, x.Description, x.Status, x.Lines.Sum(l => l.Debit), x.Lines.Sum(l => l.Credit)))
            .ToListAsync();
    }

    public async Task<JournalEntry?> GetJournalEntryAsync(int id)
    {
        return await journalService.GetAsync(id);
    }

    public async Task<JournalEntry> SaveJournalAsync(ManualJournalDto journal)
    {
        var ledger = await GetDefaultLedgerAsync();
        var request = new CreateJournalEntryDto(
            journal.JournalNo ?? string.Empty,
            journal.EntryDate,
            journal.Description,
            journal.Lines.Select(line => new CreateJournalLineDto(line.AccountId, line.Direction == "Debit" ? line.Amount : 0, line.Direction == "Credit" ? line.Amount : 0, line.Description)).ToList());

        if (journal.Id.HasValue)
        {
            var result = await journalService.UpdateAsync(journal.Id.Value, request);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Error ?? "Unable to update journal.");
            }

            return result.Entry ?? await journalService.GetAsync(journal.Id.Value) ?? throw new InvalidOperationException("Journal not found.");
        }

        return await journalService.CreateAsync(ledger.Id, request);
    }

    public async Task PostJournalAsync(int id)
    {
        var result = await journalService.PostAsync(id);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Unable to post journal.");
        }
    }

    public async Task DeleteDraftJournalAsync(int id)
    {
        var result = await journalService.DeleteAsync(id);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Unable to delete journal.");
        }
    }

    public async Task ReversePostedJournalAsync(int id)
    {
        var original = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (original is null)
        {
            throw new InvalidOperationException("Journal entry not found.");
        }

        if (original.Status != JournalStatus.Posted)
        {
            throw new InvalidOperationException("Only posted journal entries can be reversed.");
        }

        var reversal = new JournalEntry
        {
            LedgerId = original.LedgerId,
            JournalNo = string.Empty,
            EntryDate = DateTime.Today,
            Description = $"Reversal of {original.JournalNo}",
            Status = JournalStatus.Draft,
            Lines = original.Lines.Select(line => new JournalLine
            {
                AccountId = line.AccountId,
                Debit = line.Credit,
                Credit = line.Debit,
                Description = $"Reverse: {line.Description}"
            }).ToList()
        };

        var saved = await journalService.CreateAsync(original.LedgerId, new CreateJournalEntryDto(reversal.JournalNo, reversal.EntryDate, reversal.Description, reversal.Lines.Select(line => new CreateJournalLineDto(line.AccountId, line.Debit, line.Credit, line.Description)).ToList()));
        await journalService.PostAsync(saved.Id);

        original.Status = JournalStatus.Reversed;
        await db.SaveChangesAsync();
    }

    public async Task<List<TrialBalanceRowDto>> GetTrialBalanceAsync()
    {
        var ledger = await GetDefaultLedgerAsync();
        return await reportService.GetTrialBalanceAsync(ledger.Id, null, null);
    }

    public async Task<List<GeneralLedgerRowDto>> GetGeneralLedgerAsync(int accountId)
    {
        var ledger = await GetDefaultLedgerAsync();
        return await reportService.GetGeneralLedgerAsync(ledger.Id, accountId, null, null);
    }

    public async Task<List<FinancialStatementRowDto>> GetProfitLossAsync()
    {
        return await GetStatementRowsAsync([AccountType.Income, AccountType.Revenue, AccountType.Expense]);
    }

    public async Task<List<FinancialStatementRowDto>> GetBalanceSheetAsync()
    {
        return await GetStatementRowsAsync([AccountType.Asset, AccountType.Liability, AccountType.Equity]);
    }

    private async Task<List<FinancialStatementRowDto>> GetStatementRowsAsync(AccountType[] types)
    {
        var ledger = await GetDefaultLedgerAsync();
        var accounts = await db.Accounts
            .Where(x => x.LedgerId == ledger.Id && types.Contains(x.Type))
            .OrderBy(x => x.Code)
            .ToListAsync();

        var accountIds = accounts.Select(x => x.Id).ToList();
        var postedLines = await db.JournalLines
            .Include(x => x.JournalEntry)
            .Where(x => accountIds.Contains(x.AccountId) && x.JournalEntry!.Status == JournalStatus.Posted)
            .ToListAsync();

        return accounts.Select(account =>
        {
            var debit = postedLines.Where(line => line.AccountId == account.Id).Sum(line => line.Debit);
            var credit = postedLines.Where(line => line.AccountId == account.Id).Sum(line => line.Credit);
            var balance = account.Type is AccountType.Asset or AccountType.Expense
                ? debit - credit
                : credit - debit;

            return new FinancialStatementRowDto(account.Code, account.Name, account.Type, debit, credit, balance);
        }).ToList();
    }

    public async Task<LedgerSettingsDto> GetLedgerSettingsAsync()
    {
        var ledger = await GetDefaultLedgerAsync();
        return new LedgerSettingsDto(ledger.Id, ledger.Name, "USD", "2026-06", ledger.IsActive);
    }

    public async Task SaveLedgerSettingsAsync(LedgerSettingsDto settings)
    {
        var ledger = await db.Ledgers.FirstAsync(x => x.Id == settings.LedgerId);
        ledger.Name = settings.LedgerName.Trim();
        ledger.IsActive = settings.IsActive;
        await db.SaveChangesAsync();
    }

    public async Task<NumberingRuleDto> GetJournalNumberingRuleAsync()
    {
        await GetDefaultLedgerAsync();
        return new NumberingRuleDto("JournalEntry", "JE", "yyyyMM", 4, "-", true, false, true);
    }
}

public record DashboardDto(int LedgerId, string LedgerName, string CurrentPeriod, string BaseCurrency, int AccountsCount, int DraftJournalsCount, int PostedJournalsCount, List<JournalEntryListDto> LatestJournalEntries);
public record JournalEntryListDto(int Id, string JournalNo, DateTime EntryDate, string Description, JournalStatus Status, decimal TotalDebit, decimal TotalCredit);
public record AccountEditDto(int? Id, string Code, string Name, AccountType Type, string? Description, bool IsActive);
public record ManualJournalDto(int? Id, string? JournalNo, DateTime EntryDate, string Period, string Currency, string BaseCurrency, decimal ExchangeRate, string? Description, List<ManualJournalLineDto> Lines);
public record ManualJournalLineDto(int AccountId, string Direction, decimal Amount, decimal BaseAmount, string? Description);
public record LedgerSettingsDto(int LedgerId, string LedgerName, string BaseCurrency, string CurrentPeriod, bool IsActive);
public record NumberingRuleDto(string DocumentType, string Prefix, string DateFormat, int PaddingLength, string Separator, bool ResetMonthly, bool ResetYearly, bool IsActive);
public record FinancialStatementRowDto(string AccountCode, string AccountName, AccountType AccountType, decimal Debit, decimal Credit, decimal Balance);
