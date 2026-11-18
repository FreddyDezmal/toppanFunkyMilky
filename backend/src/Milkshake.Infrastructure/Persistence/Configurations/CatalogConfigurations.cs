using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Milkshake.Domain.Catalog;
using Milkshake.Domain.Common;

namespace Milkshake.Infrastructure.Persistence.Configurations;

/// <summary>Mapping shared by flavours, toppings and consistencies.</summary>
internal abstract class CatalogItemConfiguration<TItem>(string tableName) : IEntityTypeConfiguration<TItem>
    where TItem : CatalogItem
{
    public void Configure(EntityTypeBuilder<TItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(tableName);
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(CatalogItem.NameMaxLength).IsRequired();
        // Names are unique ignoring letter case: a unique index on lower(name), created with SQL in the
        // CaseInsensitiveNamesAndRowVersions migration (EF Core cannot express expression indexes).

        ConfigurePrices(builder);
    }

    protected abstract void ConfigurePrices(EntityTypeBuilder<TItem> builder);
}

/// <summary>Mapping shared by the three price-history tables.</summary>
internal abstract class PricePeriodConfiguration<TPrice>(string tableName, string itemIdProperty) : IEntityTypeConfiguration<TPrice>
    where TPrice : PricePeriod
{
    public void Configure(EntityTypeBuilder<TPrice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(tableName, table =>
        {
            table.HasCheckConstraint($"ck_{tableName}_amount_non_negative", "amount >= 0");
            table.HasCheckConstraint($"ck_{tableName}_effective_range", "effective_to IS NULL OR effective_to > effective_from");
        });

        builder.HasKey(price => price.Id);

        builder.ComplexProperty(price => price.Price, money =>
        {
            money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(12, Money.AmountScale);
            money.Property(m => m.CurrencyCode).HasColumnName("currency_code").HasMaxLength(Money.CurrencyCodeLength).IsFixedLength();
        });

        // A given item cannot have two prices starting at the same moment.
        builder.HasIndex([itemIdProperty, nameof(PricePeriod.EffectiveFrom)], $"ux_{tableName}_item_effective_from").IsUnique();

        // At most one open-ended (current) price per item.
        builder.HasIndex([itemIdProperty], $"ux_{tableName}_one_open_price").IsUnique().HasFilter("effective_to IS NULL");
    }
}

internal sealed class FlavourConfiguration() : CatalogItemConfiguration<Flavour>("flavours")
{
    protected override void ConfigurePrices(EntityTypeBuilder<Flavour> builder) =>
        builder.HasMany(f => f.Prices).WithOne(p => p.Flavour).HasForeignKey(p => p.FlavourId).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class ToppingConfiguration() : CatalogItemConfiguration<Topping>("toppings")
{
    protected override void ConfigurePrices(EntityTypeBuilder<Topping> builder) =>
        builder.HasMany(t => t.Prices).WithOne(p => p.Topping).HasForeignKey(p => p.ToppingId).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class ConsistencyConfiguration() : CatalogItemConfiguration<Consistency>("consistencies")
{
    protected override void ConfigurePrices(EntityTypeBuilder<Consistency> builder) =>
        builder.HasMany(c => c.Prices).WithOne(p => p.Consistency).HasForeignKey(p => p.ConsistencyId).OnDelete(DeleteBehavior.Restrict);
}

internal sealed class FlavourPriceConfiguration() : PricePeriodConfiguration<FlavourPrice>("flavour_prices", nameof(FlavourPrice.FlavourId));

internal sealed class ToppingPriceConfiguration() : PricePeriodConfiguration<ToppingPrice>("topping_prices", nameof(ToppingPrice.ToppingId));

internal sealed class ConsistencyPriceConfiguration() : PricePeriodConfiguration<ConsistencyPrice>("consistency_prices", nameof(ConsistencyPrice.ConsistencyId));