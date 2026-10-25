using Books.Domain.Access;
using Books.Domain.Ledgers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Books.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("companies");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasAlternateKey(x => new { x.OrgId, x.Id });
        b.HasIndex(x => new { x.OrgId, x.CreatedAt, x.Id });
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Restrict);
        // The reverse FK (companies → ledgers) is created in SQL by the migration; both directions are deferred.
    }
}

internal sealed class LedgerConfiguration : IEntityTypeConfiguration<Ledger>
{
    public void Configure(EntityTypeBuilder<Ledger> b)
    {
        b.ToTable("ledgers", t => t.HasCheckConstraint("ck_ledgers_fy_start_month", "fy_start_month BETWEEN 1 AND 12"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.BaseCurrency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.TaxPack).HasMaxLength(50);
        b.HasAlternateKey(x => new { x.OrgId, x.Id });
        b.HasAlternateKey(x => new { x.OrgId, x.CompanyId });
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.HasOne<Company>().WithOne()
            .HasForeignKey<Ledger>(x => new { x.OrgId, x.CompanyId })
            .HasPrincipalKey<Company>(x => new { x.OrgId, x.Id })
            .HasConstraintName("fk_ledgers_companies")
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LedgerGrantConfiguration : IEntityTypeConfiguration<LedgerGrant>
{
    public void Configure(EntityTypeBuilder<LedgerGrant> b)
    {
        b.ToTable("ledger_grants", t => t.HasCheckConstraint("ck_ledger_grants_access", "access IN ('read', 'edit')"));
        b.HasKey(x => new { x.MembershipId, x.LedgerId });
        b.HasIndex(x => new { x.OrgId, x.LedgerId });
        // Composite keys keep a grant's member and ledger inside the same organization.
        b.HasOne<Membership>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.MembershipId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.GrantedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("accounts", t => t.HasCheckConstraint(
            "ck_accounts_type", "type IN ('assets', 'liabilities', 'equity', 'income', 'cost_and_expense')"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Ignore(x => x.Postable);
        b.Property(x => x.Code).HasMaxLength(Account.MaxCodeLength);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        // Rule 16: Code is unique within a ledger ignoring case; other ledgers may reuse it.
        // The unique index is on (ledger_id, lower(code)) and is created in SQL by the migration.
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // The composite key keeps the group of an account inside the same ledger.
        b.HasOne<AccountGroup>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.GroupId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountGroupConfiguration : IEntityTypeConfiguration<AccountGroup>
{
    public void Configure(EntityTypeBuilder<AccountGroup> b)
    {
        b.ToTable("account_groups", t => t.HasCheckConstraint(
            "ck_account_groups_type", "type IN ('assets', 'liabilities', 'equity', 'income', 'cost_and_expense')"));
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // The composite key keeps a parent group inside the same ledger.
        b.HasOne<AccountGroup>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.ParentId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountPurposeConfiguration : IEntityTypeConfiguration<AccountPurpose>
{
    public void Configure(EntityTypeBuilder<AccountPurpose> b)
    {
        b.ToTable("account_purposes", t => t.HasCheckConstraint(
            "ck_account_purposes_purpose", "purpose IN ('retained_earnings', 'opening_balance_clearing', 'rounding_differences', 'suspense')"));
        b.HasKey(x => new { x.LedgerId, x.Purpose });
        b.HasIndex(x => new { x.LedgerId, x.AccountId });
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.AccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> b)
    {
        b.ToTable("parties");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.TaxId).HasMaxLength(50);
        b.Property(x => x.ContactName).HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(320);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Address).HasMaxLength(500);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TaxCodeConfiguration : IEntityTypeConfiguration<TaxCode>
{
    public void Configure(EntityTypeBuilder<TaxCode> b)
    {
        b.ToTable("tax_codes", t =>
        {
            t.HasCheckConstraint("ck_tax_codes_kind", "kind IN ('vat_output', 'vat_input', 'vat_input_non_deductible', 'vat_zero_rated', 'non_vat', 'withholding', 'other_tax')");
            t.HasCheckConstraint("ck_tax_codes_rate", "rate >= 0 AND rate <= 100");
        });
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Rate).HasPrecision(9, 4);
        b.Property(x => x.ReturnCategory).HasMaxLength(40);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.AccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable("bank_accounts");
        b.HasKey(x => x.AccountId);
        b.Property(x => x.BankName).HasMaxLength(200);
        b.Property(x => x.AccountNumber).HasMaxLength(50);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.HasIndex(x => x.LedgerId);
        // One bank account per ledger account, in the same ledger.
        b.HasOne<Account>().WithOne()
            .HasForeignKey<BankAccount>(x => new { x.LedgerId, x.AccountId }).HasPrincipalKey<Account>(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.ToTable("exchange_rates", t => t.HasCheckConstraint("ck_exchange_rates_rate", "rate > 0"));
        b.HasKey(x => new { x.OrgId, x.Date, x.FromCurrency, x.ToCurrency });
        b.Property(x => x.FromCurrency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.ToCurrency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.Rate).HasPrecision(20, 10);
        b.Property(x => x.Source).HasMaxLength(50);
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrgId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> b)
    {
        b.ToTable("item_categories");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ItemComponentConfiguration : IEntityTypeConfiguration<ItemComponent>
{
    public void Configure(EntityTypeBuilder<ItemComponent> b)
    {
        b.ToTable("item_components", t => t.HasCheckConstraint("ck_item_components_quantity", "quantity > 0 AND component_id <> item_id"));
        b.HasKey(x => new { x.ItemId, x.ComponentId });
        b.Property(x => x.Quantity).HasPrecision(20, 4);
        b.HasOne<Item>().WithMany().HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ItemUnitConfiguration : IEntityTypeConfiguration<ItemUnit>
{
    public void Configure(EntityTypeBuilder<ItemUnit> b)
    {
        b.ToTable("item_units", t => t.HasCheckConstraint("ck_item_units_factor", "factor > 0"));
        b.HasKey(x => new { x.ItemId, x.Name });
        b.Property(x => x.Name).HasMaxLength(20);
        b.Property(x => x.Factor).HasPrecision(20, 4);
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items");
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.Code).HasMaxLength(50);
        b.Property(x => x.SalesPrice).HasPrecision(20, 4);
        b.Property(x => x.PurchasePrice).HasPrecision(20, 4);
        b.Property(x => x.SalesDescription).HasMaxLength(1000);
        b.Property(x => x.PurchaseDescription).HasMaxLength(1000);
        b.HasIndex(x => new { x.LedgerId, x.Code }).IsUnique().HasFilter("code IS NOT NULL");
        b.HasMany(x => x.Units).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Components).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Components).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasAlternateKey(x => new { x.LedgerId, x.Id });
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        // Composite keys keep category, default account and default tax code in the same ledger.
        b.HasOne<ItemCategory>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.CategoryId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.DefaultAccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TaxCode>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.DefaultTaxCodeId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        foreach (var code in new[] { nameof(Item.SalesTaxCodeId), nameof(Item.PurchaseTaxCodeId) })
        {
            b.HasOne<TaxCode>().WithMany()
                .HasForeignKey(nameof(Item.LedgerId), code).HasPrincipalKey(nameof(TaxCode.LedgerId), nameof(TaxCode.Id))
                .OnDelete(DeleteBehavior.Restrict);
        }
        foreach (var account in new[] { nameof(Item.InventoryAccountId), nameof(Item.IncomeAccountId), nameof(Item.CostOfSalesAccountId) })
        {
            b.HasOne<Account>().WithMany()
                .HasForeignKey(nameof(Item.LedgerId), account).HasPrincipalKey(nameof(Account.LedgerId), nameof(Account.Id))
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}

internal sealed class AccountUsageConfiguration : IEntityTypeConfiguration<AccountUsage>
{
    public void Configure(EntityTypeBuilder<AccountUsage> b)
    {
        b.ToTable("account_usages", t => t.HasCheckConstraint("ck_account_usages_usage",
            "usage IN ('payable', 'receivable', 'bank', 'output_vat', 'input_vat', 'non_deductible_vat', 'withholding_payable', "
            + "'salary_expense', 'nssf_expense', 'salaries_payable', 'salary_tax_payable', 'nssf_payable', 'other_tax_payable', 'inventory', 'cost_of_sales', "
            + "'fixed_asset', 'accumulated_depreciation', 'depreciation_expense', 'fx_realized', 'fx_unrealized', 'receivable_revaluation', 'payable_revaluation')"));
        b.HasKey(x => new { x.AccountId, x.Usage });
        b.HasIndex(x => new { x.LedgerId, x.Usage });
        b.HasOne<Account>().WithMany()
            .HasForeignKey(x => new { x.LedgerId, x.AccountId }).HasPrincipalKey(x => new { x.LedgerId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Ledger>().WithMany()
            .HasForeignKey(x => new { x.OrgId, x.LedgerId }).HasPrincipalKey(x => new { x.OrgId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
