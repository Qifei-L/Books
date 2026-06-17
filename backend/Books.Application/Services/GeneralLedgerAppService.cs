using Books.Application.DTOs;
using Books.Application.Interfaces;
using Books.Application.Reports;
using Books.Domain.Entities;
using Books.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Services;

public class GeneralLedgerAppService(
    IAppDbContext db,
    JournalService journalService,
    ReportService reportService)
{
    public async Task<Entity> GetDefaultEntityAsync()
    {
        var entity = await db.Entities
            .OrderByDescending(x => x.Code == "DEMO")
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (entity is not null)
        {
            return entity;
        }

        entity = new Entity
        {
            Code = "DEMO",
            Name = "Demo Company",
            IsActive = true
        };
        db.Entities.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    public async Task<Ledger> GetDefaultLedgerAsync()
    {
        var entity = await GetDefaultEntityAsync();
        var ledger = await db.Ledgers
            .Where(x => x.EntityId == entity.Id)
            .OrderByDescending(x => x.Code == "MAIN")
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync();
        if (ledger is not null)
        {
            return ledger;
        }

        ledger = new Ledger
        {
            EntityId = entity.Id,
            Code = "MAIN",
            Name = "Main Ledger",
            LedgerType = LedgerType.Main,
            IsActive = true,
            AllowDeletePostedJournal = true
        };
        db.Ledgers.Add(ledger);
        await db.SaveChangesAsync();
        return ledger;
    }

    public async Task<List<Entity>> GetEntitiesAsync()
    {
        return await db.Entities
            .OrderBy(x => x.Code)
            .ToListAsync();
    }

    public async Task<Entity?> GetEntityAsync(int entityId)
    {
        return await db.Entities.FirstOrDefaultAsync(x => x.Id == entityId);
    }

    public async Task<Entity> SaveEntityAsync(EntityEditDto entity)
    {
        var target = entity.Id.HasValue
            ? await db.Entities.FirstAsync(x => x.Id == entity.Id.Value)
            : new Entity();

        target.Code = entity.Code.Trim();
        target.Name = entity.Name.Trim();
        target.IsActive = entity.IsActive;

        if (!entity.Id.HasValue)
        {
            db.Entities.Add(target);
        }

        await db.SaveChangesAsync();
        return target;
    }

    public async Task<List<Ledger>> GetLedgersAsync(int? entityId = null)
    {
        var query = db.Ledgers.AsQueryable();
        if (entityId.HasValue)
        {
            query = query.Where(x => x.EntityId == entityId.Value);
        }

        return await query
            .OrderBy(x => x.EntityId)
            .ThenBy(x => x.LedgerType == LedgerType.Main ? 0 : 1)
            .ThenBy(x => x.Code)
            .ToListAsync();
    }

    public async Task<Ledger?> GetLedgerAsync(int ledgerId)
    {
        return await db.Ledgers.FirstOrDefaultAsync(x => x.Id == ledgerId);
    }

    public async Task<Ledger> SaveLedgerAsync(LedgerEditDto ledger)
    {
        var entityExists = await db.Entities.AnyAsync(x => x.Id == ledger.EntityId);
        if (!entityExists)
        {
            throw new InvalidOperationException("Entity not found.");
        }

        var target = ledger.Id.HasValue
            ? await db.Ledgers.FirstAsync(x => x.Id == ledger.Id.Value)
            : new Ledger { EntityId = ledger.EntityId };

        target.EntityId = ledger.EntityId;
        target.Code = ledger.Code.Trim();
        target.Name = ledger.Name.Trim();
        target.LedgerType = ledger.LedgerType;
        target.IsActive = ledger.IsActive;
        target.AllowDeletePostedJournal = ledger.AllowDeletePostedJournal;

        if (!ledger.Id.HasValue)
        {
            db.Ledgers.Add(target);
        }

        await db.SaveChangesAsync();
        return target;
    }

    public async Task<DashboardDto> GetDashboardAsync()
    {
        var ledger = await GetDefaultLedgerAsync();
        var accountsCount = await db.Accounts.CountAsync(x => x.EntityId == ledger.EntityId);
        var draftCount = await db.JournalEntries.CountAsync(x => x.LedgerId == ledger.Id && x.Status == JournalStatus.Draft);
        var postedCount = await db.JournalEntries.CountAsync(x => x.LedgerId == ledger.Id && x.Status == JournalStatus.Posted);
        var latestEntries = await db.JournalEntries
            .Where(x => x.LedgerId == ledger.Id && x.Status != JournalStatus.Voided)
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
        return await GetAccountsAsync(ledger.EntityId, search, type, isActive);
    }

    public async Task<List<Account>> GetAccountsAsync(int entityId, string? search, AccountType? type, bool? isActive)
    {
        var query = db.Accounts.Where(x => x.EntityId == entityId);

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
        var entityExists = await db.Entities.AnyAsync(x => x.Id == account.EntityId);
        if (!entityExists)
        {
            throw new InvalidOperationException("Entity not found.");
        }

        var entity = account.Id.HasValue
            ? await db.Accounts.FirstAsync(x => x.Id == account.Id.Value && x.EntityId == account.EntityId)
            : new Account { EntityId = account.EntityId, IsSystemReserved = false };

        if (!entity.IsSystemReserved)
        {
            entity.Code = account.Code.Trim();
            entity.Type = account.Type;
        }

        entity.Name = account.Name.Trim();
        entity.Description = account.Description?.Trim() ?? string.Empty;
        entity.IsActive = account.IsActive;
        entity.AllowManualJournal = account.AllowManualJournal;

        if (!account.Id.HasValue)
        {
            db.Accounts.Add(entity);
        }

        await db.SaveChangesAsync();
    }

    public async Task<List<Account>> GetManualJournalAccountsAsync()
    {
        var ledger = await GetDefaultLedgerAsync();
        return await GetManualJournalAccountsAsync(ledger.Id);
    }

    public async Task<List<Account>> GetManualJournalAccountsAsync(int ledgerId)
    {
        var ledger = await db.Ledgers.FirstAsync(x => x.Id == ledgerId);
        return await db.Accounts
            .Where(x => x.EntityId == ledger.EntityId && x.IsActive && x.AllowManualJournal)
            .OrderBy(x => x.Code)
            .ToListAsync();
    }

    public async Task ToggleAccountAsync(int id)
    {
        var ledger = await GetDefaultLedgerAsync();
        await ToggleAccountAsync(ledger.EntityId, id);
    }

    public async Task ToggleAccountAsync(int entityId, int id)
    {
        var account = await db.Accounts.FirstAsync(x => x.Id == id && x.EntityId == entityId);
        account.IsActive = !account.IsActive;
        await db.SaveChangesAsync();
    }

    public async Task<List<JournalEntryListDto>> GetJournalEntriesAsync(JournalStatus? status = null)
    {
        var ledger = await GetDefaultLedgerAsync();
        return await GetJournalEntriesAsync(ledger.Id, status);
    }

    public async Task<List<JournalEntryListDto>> GetJournalEntriesAsync(int ledgerId, JournalStatus? status = null)
    {
        var query = db.JournalEntries.Where(x => x.LedgerId == ledgerId);

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
        if (journal.LedgerId <= 0)
        {
            throw new InvalidOperationException("Ledger is required.");
        }

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

        return await journalService.CreateAsync(journal.LedgerId, request);
    }

    public async Task PostJournalAsync(int id)
    {
        var result = await journalService.PostAsync(id);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Unable to post journal.");
        }
    }

    public async Task DeleteJournalAsync(int id)
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

        var saved = await journalService.CreateAsync(original.LedgerId, new CreateJournalEntryDto(reversal.JournalNo, reversal.EntryDate, reversal.Description, reversal.Lines.Select(line => new CreateJournalLineDto(line.AccountId, line.Debit, line.Credit, line.Description)).ToList()), enforceManualJournalAllowed: false);
        var postResult = await journalService.PostAsync(saved.Id, enforceManualJournalAllowed: false);
        if (!postResult.Success)
        {
            throw new InvalidOperationException(postResult.Error ?? "Unable to post reversal entry.");
        }

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
        var ledger = await GetDefaultLedgerAsync();
        return await reportService.GetProfitLossAsync(ledger.Id, null, null);
    }

    public async Task<List<FinancialStatementRowDto>> GetBalanceSheetAsync()
    {
        var ledger = await GetDefaultLedgerAsync();
        return await reportService.GetBalanceSheetAsync(ledger.Id, null, null);
    }

    public async Task<LedgerSettingsDto> GetLedgerSettingsAsync()
    {
        var ledger = await GetDefaultLedgerAsync();
        var entity = await db.Entities.FirstAsync(x => x.Id == ledger.EntityId);
        return new LedgerSettingsDto(
            entity.Id,
            entity.Code,
            entity.Name,
            ledger.Id,
            ledger.Code,
            ledger.Name,
            ledger.LedgerType,
            "USD",
            "2026-06",
            ledger.IsActive,
            ledger.AllowDeletePostedJournal);
    }

    public async Task SaveLedgerSettingsAsync(LedgerSettingsDto settings)
    {
        var ledger = await db.Ledgers.Include(x => x.Entity).FirstAsync(x => x.Id == settings.LedgerId);
        ledger.Entity!.Code = settings.EntityCode.Trim();
        ledger.Entity.Name = settings.EntityName.Trim();
        ledger.Code = settings.LedgerCode.Trim();
        ledger.Name = settings.LedgerName.Trim();
        ledger.LedgerType = settings.LedgerType;
        ledger.IsActive = settings.IsActive;
        ledger.AllowDeletePostedJournal = settings.AllowDeletePostedJournal;
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
public record EntityEditDto(int? Id, string Code, string Name, bool IsActive);
public record LedgerEditDto(int? Id, int EntityId, string Code, string Name, LedgerType LedgerType, bool IsActive, bool AllowDeletePostedJournal);
public record AccountEditDto(int? Id, int EntityId, string Code, string Name, AccountType Type, string? Description, bool IsActive, bool AllowManualJournal);
public record ManualJournalDto(int? Id, int LedgerId, string? JournalNo, DateTime EntryDate, string Period, string Currency, string BaseCurrency, decimal ExchangeRate, string? Description, List<ManualJournalLineDto> Lines);
public record ManualJournalLineDto(int AccountId, string Direction, decimal Amount, decimal BaseAmount, string? Description);
public record LedgerSettingsDto(
    int EntityId,
    string EntityCode,
    string EntityName,
    int LedgerId,
    string LedgerCode,
    string LedgerName,
    LedgerType LedgerType,
    string BaseCurrency,
    string CurrentPeriod,
    bool IsActive,
    bool AllowDeletePostedJournal);
public record NumberingRuleDto(string DocumentType, string Prefix, string DateFormat, int PaddingLength, string Separator, bool ResetMonthly, bool ResetYearly, bool IsActive);
