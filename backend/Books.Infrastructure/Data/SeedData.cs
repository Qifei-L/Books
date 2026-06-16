using Books.Domain.Entities;
using Books.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Books.Infrastructure.Data;

public static class SeedData
{
    public static async Task InitializeAsync(AppDbContext db)
    {
        var entity = await db.Entities.SingleOrDefaultAsync(x => x.Code == "DEMO");
        if (entity is null)
        {
            entity = new Entity
            {
                Code = "DEMO",
                Name = "Demo Company",
                IsActive = true
            };
            db.Entities.Add(entity);
            await db.SaveChangesAsync();
        }
        else
        {
            entity.Name = "Demo Company";
            entity.IsActive = true;
        }

        var ledgers = new[]
        {
            new LedgerSeed("MAIN", "Main Ledger", LedgerType.Main),
            new LedgerSeed("KH-COMPLY", "Cambodia Compliant Ledger", LedgerType.Compliant),
            new LedgerSeed("TAX-ADJ", "Tax Adjustment Ledger", LedgerType.Tax)
        };

        foreach (var seed in ledgers)
        {
            var ledger = await db.Ledgers.FirstOrDefaultAsync(x => x.EntityId == entity.Id && x.Code == seed.Code);
            if (ledger is null && seed.Code == "MAIN")
            {
                ledger = await db.Ledgers.FirstOrDefaultAsync(x => x.EntityId == entity.Id && x.Name == "Demo Ledger");
            }

            if (ledger is null)
            {
                db.Ledgers.Add(new Ledger
                {
                    EntityId = entity.Id,
                    Code = seed.Code,
                    Name = seed.Name,
                    LedgerType = seed.LedgerType,
                    IsActive = true,
                    AllowDeletePostedJournal = true
                });
                continue;
            }

            ledger.Code = seed.Code;
            ledger.Name = seed.Name;
            ledger.LedgerType = seed.LedgerType;
            ledger.IsActive = true;
            ledger.AllowDeletePostedJournal = true;
        }

        await db.SaveChangesAsync();

        var accountSeeds = new[]
        {
            new AccountSeed("1000", "Cash", AccountType.Asset, IsSystemReserved: false, AllowManualJournal: true),
            new AccountSeed("1010", "Bank", AccountType.Asset, IsSystemReserved: false, AllowManualJournal: true),
            new AccountSeed("1200", "Accounts Receivable", AccountType.Asset, IsSystemReserved: false, AllowManualJournal: true),
            new AccountSeed("2000", "Accounts Payable", AccountType.Liability, IsSystemReserved: false, AllowManualJournal: true),
            new AccountSeed("3000", "Retained Earnings", AccountType.Equity, IsSystemReserved: true, AllowManualJournal: false),
            new AccountSeed("3100", "Current Year Profit & Loss", AccountType.Equity, IsSystemReserved: true, AllowManualJournal: false),
            new AccountSeed("4000", "Sales Revenue", AccountType.Revenue, IsSystemReserved: false, AllowManualJournal: true),
            new AccountSeed("5000", "General Expense", AccountType.Expense, IsSystemReserved: false, AllowManualJournal: true),
            new AccountSeed("8000", "Exchange Gain or Loss", AccountType.Revenue, IsSystemReserved: true, AllowManualJournal: true),
            new AccountSeed("9999", "Suspense Account", AccountType.Asset, IsSystemReserved: true, AllowManualJournal: true)
        };

        foreach (var seed in accountSeeds)
        {
            var account = await db.Accounts.FirstOrDefaultAsync(x => x.EntityId == entity.Id && x.Code == seed.Code);
            if (account is null && seed.Code == "1200")
            {
                account = await db.Accounts.FirstOrDefaultAsync(x => x.EntityId == entity.Id && x.Code == "1100" && x.Name == "Accounts Receivable");
            }

            if (account is null)
            {
                db.Accounts.Add(new Account
                {
                    EntityId = entity.Id,
                    Code = seed.Code,
                    Name = seed.Name,
                    Type = seed.Type,
                    IsActive = true,
                    IsSystemReserved = seed.IsSystemReserved,
                    AllowManualJournal = seed.AllowManualJournal
                });
                continue;
            }

            account.Code = seed.Code;
            account.Name = seed.Name;
            account.Type = seed.Type;
            account.IsActive = true;
            account.IsSystemReserved = seed.IsSystemReserved;
            account.AllowManualJournal = seed.AllowManualJournal;
        }

        await db.SaveChangesAsync();

        var mainLedger = await db.Ledgers.SingleAsync(x => x.EntityId == entity.Id && x.Code == "MAIN");
        var journalExists = await db.JournalEntries
            .AnyAsync(x => x.LedgerId == mainLedger.Id && x.JournalNo == "JV-000001");
        if (journalExists)
        {
            return;
        }

        var cash = await db.Accounts.SingleAsync(x => x.EntityId == entity.Id && x.Code == "1000");
        var equity = await db.Accounts.SingleAsync(x => x.EntityId == entity.Id && x.Code == "3000");

        db.JournalEntries.Add(new JournalEntry
        {
            LedgerId = mainLedger.Id,
            JournalNo = "JV-000001",
            EntryDate = DateTime.Today,
            Description = "Owner capital injection",
            Status = JournalStatus.Posted,
            Lines =
            [
                new JournalLine { AccountId = cash.Id, Debit = 1000, Credit = 0, Description = "Dr Cash" },
                new JournalLine { AccountId = equity.Id, Debit = 0, Credit = 1000, Description = "Cr Retained Earnings" }
            ]
        });
        await db.SaveChangesAsync();
    }

    private sealed record LedgerSeed(string Code, string Name, LedgerType LedgerType);

    private sealed record AccountSeed(
        string Code,
        string Name,
        AccountType Type,
        bool IsSystemReserved,
        bool AllowManualJournal);
}
