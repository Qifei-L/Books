using Books.Domain.Access;
using Books.Domain.Journals;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class JournalConfiguration : IEntityTypeConfiguration<Journal>
{
    public void Configure(EntityTypeBuilder<Journal> b)
    {
        b.ToTable("journals");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.IsPosted);
        b.Property(x => x.SourceKind).HasMaxLength(50);
        // A document has at most one journal.
        b.HasIndex(x => new { x.SourceKind, x.SourceId }).IsUnique().HasFilter("source_kind IS NOT NULL");
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        // Rule 24: Journal No. is unique per ledger.
        b.HasIndex(x => new { x.LedgerId, x.JournalNo }).IsUnique();
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        // effective_revision_id and current_revision_id reference journal_revisions through deferred keys created in SQL.
    }
}

internal sealed class JournalRevisionConfiguration : IEntityTypeConfiguration<JournalRevision>
{
    public void Configure(EntityTypeBuilder<JournalRevision> b)
    {
        b.ToTable("journal_revisions", t =>
        {
            t.HasCheckConstraint("ck_journal_revisions_kind", "kind IN ('manual', 'opening')");
            t.HasCheckConstraint("ck_journal_revisions_status", "status IN ('draft', 'error', 'posted', 'superseded', 'deleted')");
        });
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Reference).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Issues).HasColumnType("jsonb");
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasAlternateKey(x => new { x.JournalId, x.Id });
        b.HasIndex(x => new { x.JournalId, x.RevisionNo }).IsUnique();
        b.HasOne<Journal>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.JournalId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> b)
    {
        b.ToTable("journal_lines", t => t.HasCheckConstraint("ck_journal_lines_amount", "amount <> 0"));
        b.HasKey(x => new { x.RevisionId, x.LineNo });
        b.Property(x => x.Amount).HasPrecision(20, 4);
        b.Property(x => x.CurrencyAmount).HasPrecision(20, 4);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.HasIndex(x => new { x.LedgerId, x.AccountId });
        b.HasIndex(x => x.LineId);
        // Composite keys keep a line, its revision and its account in the same ledger.
        b.HasOne<JournalRevision>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.RevisionId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.AccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Party>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.PartyId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Quantity).HasPrecision(20, 4);
        b.HasOne<Item>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.ItemId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
