# Entities, Keys & Columns

Schemas are defined using `IEntityRegistry` and `IEntityConfiguration<TEntity>`—no EF Core dependency required.

## Entity Registries & Configurations

```csharp
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;

public sealed class OrderConfiguration : IEntityConfiguration<Order>
{
    public void Configure(EntityBuilder<Order> builder)
    {
        builder.ToTable("orders", schema: "sales");
        builder.HasComment("Customer purchase orders");
        builder.HasKey(x => x.Id).HasName("pk_orders");
        builder.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
        builder.Ignore(x => x.TransientTotal);
    }
}

public sealed class SalesRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry)
    {
        registry.HasDefaultSchema("sales");
        registry.Map<Order, OrderConfiguration>();
        registry.Map<OrderLine>(); // Convention-only mapping
        registry.Entity<Customer>(e => e.ToTable("customers")); // Inline config
    }
}
```

::: tip Compile-Time Pairing
When using `[EntityRegistry]` and `[EntityConfiguration<T>]`, the Roslyn source generator emits all `registry.Map<...>()` calls automatically. See [Compile-Time Discovery](/migrator/source-generator).
:::

## Table & Entity Configuration

| Method | Purpose |
| --- | --- |
| `ToTable(name, schema?)` | Sets physical table name and optional schema |
| `HasPreviousTableName(oldName, oldSchema?)` | Hints table rename during `diff` |
| `HasComment(comment)` | Emits `COMMENT ON TABLE` |
| `Ignore(x => x.Prop)` | Excludes property from schema & DDL |
| `HasQueryFilter(x => ...)` | Stored on `DatabaseSchema` for `EfCoreModelBridge` |
| `HasQueryFilter(key, x => ...)` | Named query filter for EF Core 10+ |
| `HasKey(x => x.Id)` | Single or composite PK (`HasKey(x => new { x.A, x.B })`) |
| `HasKey(...).HasName("pk_...")` | Explicit primary key constraint name |

## Column Configuration (`Property(x => x.Prop)`)

```csharp
builder.Property(x => x.Code)
    .HasColumnName("order_code")
    .HasPreviousColumnName("legacy_code")
    .HasColumnType("varchar(32)")
    .HasMaxLength(32)
    .IsRequired()
    .IsUnicode(false)
    .HasDefaultValue("ORD-NEW")
    .HasComment("Unique human-readable order code");

builder.Property(x => x.Amount)
    .HasPrecision(18, 2)
    .HasDefaultValueSql("0.00");

builder.Property(x => x.FullTotal)
    .HasComputedColumnSql(
        cc => cc.Column(x => x.Subtotal).Math().Plus(x => x.Tax),
        stored: true);

// Or pass raw SQL:
// builder.Property(x => x.FullTotal).HasComputedColumnSql("\"subtotal\" + \"tax\"", stored: true);

builder.Property(x => x.Id)
    .ValueGeneratedOnAdd(); // Identity / serial

builder.Property(x => x.RowVersion)
    .IsRowVersion(); // Uses PostgreSQL xmin when property is uint
```

### Column Methods Reference

* **Naming & Types:** `HasColumnName`, `HasPreviousColumnName`, `HasColumnType`, `HasMaxLength`, `HasPrecision(precision, scale)`, `IsUnicode(bool)`, `IsRequired(bool)`
* **Defaults & Generation:** `HasDefaultValue(obj)`, `HasDefaultValueSql(sql)`, `HasComputedColumnSql(sql, stored)`, `ValueGeneratedOnAdd(isIdentity: true)`, `ValueGeneratedOnAddOrUpdate()`, `ValueGeneratedNever()`
* **Concurrency & Metadata:** `IsConcurrencyToken()`, `IsRowVersion()`, `HasComment(text)`
* **Type Conversions:** `HasConversion<TProvider>()` / `HasConversion(Type)` (built into `ColumnBuilder`), plus `HasConversion(ValueConverter)` via `SharpPersistence.Migrator.EfCore`—see [Runtime Model Bridge](/migrator-efcore/model-bridge)
