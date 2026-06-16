using Books.Application.DTOs;
using Books.Application.Interfaces;
using Books.Domain.Entities;
using Books.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Books.Application.Reports;

public class ReportService(IAppDbContext db)
{
    public Task<List<ReportSummaryDto>> GetAvailableReportsAsync(int ledgerId)
    {
        var reports = new List<ReportSummaryDto>
        {
            new("trial-balance", "Trial Balance", "Debit and credit balances by account.", $"/api/v1/ledgers/{ledgerId}/reports/trial-balance"),
            new("general-ledger", "General Ledger", "Posted journal lines and running balance for one account.", $"/api/v1/ledgers/{ledgerId}/reports/general-ledger"),
            new("profit-loss", "Profit & Loss", "Income, revenue, and expense balances.", $"/api/v1/ledgers/{ledgerId}/reports/profit-loss"),
            new("balance-sheet", "Balance Sheet", "Asset, liability, and equity balances.", $"/api/v1/ledgers/{ledgerId}/reports/balance-sheet"),
        };

        return Task.FromResult(reports);
    }

    public async Task<List<TrialBalanceRowDto>> GetTrialBalanceAsync(int ledgerId, DateTime? from, DateTime? to)
    {
        var accounts = await db.Accounts
            .Where(x => x.LedgerId == ledgerId)
            .OrderBy(x => x.Code)
            .ToListAsync();

        var postedLines = await PostedLinesQuery(ledgerId, from, to).ToListAsync();

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

        var linesQuery = PostedLinesQuery(ledgerId, from, to)
            .Where(x => x.AccountId == accountId);

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

    public Task<List<FinancialStatementRowDto>> GetProfitLossAsync(int ledgerId, DateTime? from, DateTime? to)
    {
        return GetStatementRowsAsync(ledgerId, [AccountType.Income, AccountType.Revenue, AccountType.Expense], from, to);
    }

    public Task<List<FinancialStatementRowDto>> GetBalanceSheetAsync(int ledgerId, DateTime? from, DateTime? to)
    {
        return GetStatementRowsAsync(ledgerId, [AccountType.Asset, AccountType.Liability, AccountType.Equity], from, to);
    }

    private IQueryable<JournalLine> PostedLinesQuery(int ledgerId, DateTime? from, DateTime? to)
    {
        var query = db.JournalLines
            .Include(x => x.JournalEntry)
            .Where(x => x.JournalEntry!.LedgerId == ledgerId && x.JournalEntry.Status == JournalStatus.Posted);

        if (from.HasValue)
        {
            query = query.Where(x => x.JournalEntry!.EntryDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.JournalEntry!.EntryDate <= to.Value);
        }

        return query;
    }

    private async Task<List<FinancialStatementRowDto>> GetStatementRowsAsync(
        int ledgerId,
        AccountType[] types,
        DateTime? from,
        DateTime? to)
    {
        var accounts = await db.Accounts
            .Where(x => x.LedgerId == ledgerId && types.Contains(x.Type))
            .OrderBy(x => x.Code)
            .ToListAsync();

        var accountIds = accounts.Select(x => x.Id).ToList();
        var postedLines = await PostedLinesQuery(ledgerId, from, to)
            .Where(x => accountIds.Contains(x.AccountId))
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
}
