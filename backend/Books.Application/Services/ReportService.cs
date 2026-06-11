using Books.Application.DTOs;
using Books.Application.Interfaces;
using Books.Domain.Entities;
using Books.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Services;

public class ReportService(IAppDbContext db)
{
    public async Task<List<TrialBalanceRowDto>> GetTrialBalanceAsync(int ledgerId, DateTime? from, DateTime? to)
    {
        var accounts = await db.Accounts
            .Where(x => x.LedgerId == ledgerId)
            .OrderBy(x => x.Code)
            .ToListAsync();

        var postedLinesQuery = db.JournalLines
            .Include(x => x.JournalEntry)
            .Where(x => x.JournalEntry!.LedgerId == ledgerId && x.JournalEntry.Status == JournalStatus.Posted);

        if (from.HasValue)
        {
            postedLinesQuery = postedLinesQuery.Where(x => x.JournalEntry!.EntryDate >= from.Value);
        }

        if (to.HasValue)
        {
            postedLinesQuery = postedLinesQuery.Where(x => x.JournalEntry!.EntryDate <= to.Value);
        }

        var postedLines = await postedLinesQuery.ToListAsync();

        return accounts.Select(account =>
        {
            var balance = postedLines
                .Where(line => line.AccountId == account.Id)
                .Sum(line => line.Debit - line.Credit);
            return new TrialBalanceRowDto(
                account.Code,
                account.Name,
                Math.Max(balance, 0),
                Math.Max(-balance, 0));
        }).ToList();
    }

    public async Task<List<GeneralLedgerRowDto>> GetGeneralLedgerAsync(int ledgerId, int accountId, DateTime? from, DateTime? to)
    {
        var accountBelongsToLedger = await db.Accounts.AnyAsync(x => x.Id == accountId && x.LedgerId == ledgerId);
        if (!accountBelongsToLedger)
        {
            return [];
        }

        var linesQuery = db.JournalLines
            .Include(x => x.JournalEntry)
            .Where(x => x.AccountId == accountId &&
                        x.JournalEntry!.LedgerId == ledgerId &&
                        x.JournalEntry.Status == JournalStatus.Posted);

        if (from.HasValue)
        {
            linesQuery = linesQuery.Where(x => x.JournalEntry!.EntryDate >= from.Value);
        }

        if (to.HasValue)
        {
            linesQuery = linesQuery.Where(x => x.JournalEntry!.EntryDate <= to.Value);
        }

        var lines = await linesQuery
            .OrderBy(x => x.JournalEntry!.EntryDate)
            .ThenBy(x => x.JournalEntry!.JournalNo)
            .ThenBy(x => x.Id)
            .ToListAsync();

        decimal balance = 0;
        return lines.Select(line =>
        {
            balance += line.Debit - line.Credit;
            return new GeneralLedgerRowDto(
                line.JournalEntry!.EntryDate,
                line.JournalEntry.JournalNo,
                line.Description.Length > 0 ? line.Description : line.JournalEntry.Description,
                line.Debit,
                line.Credit,
                balance);
        }).ToList();
    }
}
