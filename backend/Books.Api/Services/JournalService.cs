using Books.Api.Data;
using Books.Api.DTOs;
using Books.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Books.Api.Services;

public class JournalService(AppDbContext db)
{
    public async Task<JournalEntry> CreateAsync(int ledgerId, CreateJournalEntryDto dto)
    {
        var entry = Map(ledgerId, dto);
        ValidateLines(entry);
        await ValidateLineAccountsBelongToLedgerAsync(entry);
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
        return await GetAsync(entry.Id) ?? entry;
    }

    public async Task<JournalEntry?> GetAsync(int id)
    {
        return await db.JournalEntries
            .Include(x => x.Lines)
            .ThenInclude(x => x.Account)
            .FirstOrDefaultAsync(x => x.Id == id);
    }

    public async Task<(bool Success, string? Error, JournalEntry? Entry)> UpdateAsync(int id, CreateJournalEntryDto dto)
    {
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null)
        {
            return (false, "Journal entry not found.", null);
        }

        if (entry.Status == JournalStatus.Posted)
        {
            return (false, "Posted journal entries cannot be modified.", entry);
        }

        entry.JournalNo = dto.JournalNo.Trim();
        entry.EntryDate = dto.EntryDate;
        entry.Description = dto.Description?.Trim() ?? string.Empty;
        entry.Lines.Clear();
        foreach (var line in dto.Lines)
        {
            entry.Lines.Add(new JournalLine
            {
                AccountId = line.AccountId,
                Debit = line.Debit,
                Credit = line.Credit,
                Description = line.Description?.Trim() ?? string.Empty
            });
        }

        ValidateLines(entry);
        await ValidateLineAccountsBelongToLedgerAsync(entry);
        await db.SaveChangesAsync();
        return (true, null, await GetAsync(id));
    }

    public async Task<(bool Success, string? Error, JournalEntry? Entry)> PostAsync(int id)
    {
        var entry = await db.JournalEntries.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (entry is null)
        {
            return (false, "Journal entry not found.", null);
        }

        if (entry.Status == JournalStatus.Posted)
        {
            return (false, "Journal entry has already been posted.", entry);
        }

        ValidateLines(entry);
        await ValidateLineAccountsBelongToLedgerAsync(entry);
        entry.Status = JournalStatus.Posted;
        await db.SaveChangesAsync();
        return (true, null, await GetAsync(id));
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(int id)
    {
        var entry = await db.JournalEntries.FindAsync(id);
        if (entry is null)
        {
            return (false, "Journal entry not found.");
        }

        if (entry.Status == JournalStatus.Posted)
        {
            return (false, "Posted journal entries cannot be deleted.");
        }

        db.JournalEntries.Remove(entry);
        await db.SaveChangesAsync();
        return (true, null);
    }

    private static JournalEntry Map(int ledgerId, CreateJournalEntryDto dto)
    {
        return new JournalEntry
        {
            LedgerId = ledgerId,
            JournalNo = dto.JournalNo.Trim(),
            EntryDate = dto.EntryDate,
            Description = dto.Description?.Trim() ?? string.Empty,
            Status = JournalStatus.Draft,
            Lines = dto.Lines.Select(line => new JournalLine
            {
                AccountId = line.AccountId,
                Debit = line.Debit,
                Credit = line.Credit,
                Description = line.Description?.Trim() ?? string.Empty
            }).ToList()
        };
    }

    private async Task ValidateLineAccountsBelongToLedgerAsync(JournalEntry entry)
    {
        var accountIds = entry.Lines.Select(x => x.AccountId).Distinct().ToList();

        var existingCount = await db.Accounts.CountAsync(a => accountIds.Contains(a.Id));
        if (existingCount != accountIds.Count)
        {
            throw new InvalidOperationException("Journal entry contains unknown accounts.");
        }

        var invalidAccountExists = await db.Accounts
            .AnyAsync(a => accountIds.Contains(a.Id) && a.LedgerId != entry.LedgerId);

        if (invalidAccountExists)
        {
            throw new InvalidOperationException("Journal entry contains accounts from another ledger.");
        }
    }

    private static void ValidateLines(JournalEntry entry)
    {
        if (entry.Lines.Count < 2)
        {
            throw new InvalidOperationException("A journal entry must have at least two lines.");
        }

        foreach (var line in entry.Lines)
        {
            if (line.Debit < 0 || line.Credit < 0)
            {
                throw new InvalidOperationException("Debit and Credit cannot be negative.");
            }

            if (line.Debit > 0 && line.Credit > 0)
            {
                throw new InvalidOperationException("A line cannot have both Debit and Credit.");
            }

            if (line.Debit == 0 && line.Credit == 0)
            {
                throw new InvalidOperationException("A line must have either Debit or Credit.");
            }
        }

        var totalDebit = entry.Lines.Sum(x => x.Debit);
        var totalCredit = entry.Lines.Sum(x => x.Credit);
        if (totalDebit != totalCredit)
        {
            throw new InvalidOperationException("Total Debit must equal Total Credit.");
        }
    }
}
