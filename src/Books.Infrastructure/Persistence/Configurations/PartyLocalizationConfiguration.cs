using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class PartyLocalizationConfiguration : IEntityTypeConfiguration<PartyLocalization>
{
    public void Configure(EntityTypeBuilder<PartyLocalization> b)
    {
        b.ToTable("party_localizations", t => t.HasCheckConstraint("ck_party_localizations_locale", "locale IN ('km')"));
        b.HasKey(x => new { x.PartyId, x.Locale });
        b.Property(x => x.Locale).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(500);
        b.HasOne<Party>().WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Cascade);
    }
}
