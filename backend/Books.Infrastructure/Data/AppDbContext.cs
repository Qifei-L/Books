using Books.Application.Interfaces;
using Books.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Books.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext, IDataProtectionKeyContext
{
    public DbSet<Ledger> Ledgers => Set<Ledger>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ledger>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<Ledger>()
            .HasMany(x => x.Accounts)
            .WithOne(x => x.Ledger)
            .HasForeignKey(x => x.LedgerId);
        modelBuilder.Entity<Ledger>()
            .HasMany(x => x.JournalEntries)
            .WithOne(x => x.Ledger)
            .HasForeignKey(x => x.LedgerId);

        modelBuilder.Entity<Account>().Property(x => x.Code).HasMaxLength(30).IsRequired();
        modelBuilder.Entity<Account>().Property(x => x.Name).HasMaxLength(200).IsRequired();
        modelBuilder.Entity<Account>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<Account>().Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<Account>().Property(x => x.IsSystemReserved).HasDefaultValue(false);
        modelBuilder.Entity<Account>().Property(x => x.AllowManualJournal).HasDefaultValue(true);
        modelBuilder.Entity<Account>().HasIndex(x => new { x.LedgerId, x.Code }).IsUnique();

        modelBuilder.Entity<Ledger>().Property(x => x.AllowDeletePostedJournal).HasDefaultValue(true);

        modelBuilder.Entity<JournalEntry>().Property(x => x.JournalNo).HasMaxLength(50).IsRequired();
        modelBuilder.Entity<JournalEntry>().Property(x => x.EntryDate).HasColumnType("timestamp without time zone");
        modelBuilder.Entity<JournalEntry>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<JournalEntry>().Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        modelBuilder.Entity<JournalEntry>().HasIndex(x => new { x.LedgerId, x.JournalNo }).IsUnique();

        modelBuilder.Entity<JournalLine>().Property(x => x.Debit).HasPrecision(18, 2);
        modelBuilder.Entity<JournalLine>().Property(x => x.Credit).HasPrecision(18, 2);
        modelBuilder.Entity<JournalLine>().Property(x => x.Description).HasMaxLength(500);
        modelBuilder.Entity<JournalLine>()
            .HasOne(x => x.JournalEntry)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.JournalEntryId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<JournalLine>()
            .HasOne(x => x.Account)
            .WithMany(x => x.JournalLines)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
