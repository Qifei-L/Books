using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class AccountClassConfiguration : IEntityTypeConfiguration<AccountClass>
{
    public void Configure(EntityTypeBuilder<AccountClass> b)
    {
        b.ToTable("classes");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Code).HasMaxLength(20);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasIndex(x => new { x.LedgerId, x.Name });
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
