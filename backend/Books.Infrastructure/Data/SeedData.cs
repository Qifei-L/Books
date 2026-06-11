using Books.Domain.Entities;
using Books.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Books.Infrastructure.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        var ledger = await db.Ledgers.SingleOrDefaultAsync(x => x.Name == "Demo Ledger");
        if (ledger is null)
        {
            ledger = new Ledger { Name = "Demo Ledger", IsActive = true };
            db.Ledgers.Add(ledger);
            await db.SaveChangesAsync();
        }

        var accountSeeds = new[]
        {
            new AccountSeed("1000", "Cash", AccountType.Asset),
            new AccountSeed("1100", "Accounts Receivable", AccountType.Asset),
            new AccountSeed("2000", "Accounts Payable", AccountType.Liability),
            new AccountSeed("3000", "Owner Equity", AccountType.Equity),
            new AccountSeed("4000", "Sales Revenue", AccountType.Revenue),
            new AccountSeed("5000", "General Expense", AccountType.Expense)
        };

        foreach (var seed in accountSeeds)
        {
            var accountExists = await db.Accounts.AnyAsync(x => x.LedgerId == ledger.Id && x.Code == seed.Code);
            if (!accountExists)
            {
                db.Accounts.Add(new Account
                {
                    LedgerId = ledger.Id,
                    Code = seed.Code,
                    Name = seed.Name,
                    Type = seed.Type,
                    IsActive = true
                });
            }
        }
        await db.SaveChangesAsync();

        var journalExists = await db.JournalEntries
            .AnyAsync(x => x.LedgerId == ledger.Id && x.JournalNo == "JV-000001");
        if (journalExists)
        {
            return;
        }

        var cash = await db.Accounts.SingleAsync(x => x.LedgerId == ledger.Id && x.Code == "1000");
        var equity = await db.Accounts.SingleAsync(x => x.LedgerId == ledger.Id && x.Code == "3000");

        db.JournalEntries.Add(new JournalEntry
        {
            LedgerId = ledger.Id,
            JournalNo = "JV-000001",
            EntryDate = DateTime.Today,
            Description = "Owner capital injection",
            Status = JournalStatus.Posted,
            Lines =
            [
                new JournalLine { AccountId = cash.Id, Debit = 1000, Credit = 0, Description = "Dr Cash" },
                new JournalLine { AccountId = equity.Id, Debit = 0, Credit = 1000, Description = "Cr Owner Equity" }
            ]
        });
        await db.SaveChangesAsync();
    }

    private sealed record AccountSeed(string Code, string Name, AccountType Type);
}
