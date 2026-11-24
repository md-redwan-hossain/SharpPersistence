# Check Constraints & Sequences

## Check Constraints

You can define check constraints using either raw SQL or the type-safe `SqlCheckConstraintBuilder<TEntity>` lambda overload (which inherits your configured `SqlDialect` and `SqlIdentifierCase`):

```csharp
// Type-safe check constraint
builder.HasCheckConstraint("ck_orders_amount",
    cc => cc.Column(x => x.Amount).GreaterThan(0m));

// Conditional check constraint
builder.HasCheckConstraint("ck_orders_shipped_at",
    cc => cc.If(cc.Column(x => x.Status).EqualTo(OrderStatus.Shipped))
            .Then(cc.Column(x => x.ShippedAt).IsNotNull()));

// Raw SQL check constraint
builder.HasCheckConstraint("ck_orders_currency", "char_length(\"currency\") = 3");
```

### Per-Constraint Builder Overrides

The typed `HasCheckConstraint` overload returns `CheckConstraintBuilder<TEntity>` for per-constraint overrides:

```csharp
builder.HasCheckConstraint("ck_orders_country",
        cc => cc.NestedColumn(x => x.ShippingAddress.Country).Length().In(2, 3))
    .UseDelimitedIdentifiers(false)
    .UseNestedColumnSeparator("__");
```

You can also set defaults globally in `AddDbMigrator`:

```csharp
opt.ConfigureCheckConstraintBuilder(cc => cc
    .UseDelimitedIdentifiers(true)
    .UseNestedColumnSeparator("_"));
```

## Sequences

Define database sequences inside `IEntityRegistry.Configure`:

```csharp
public void Configure(IEntityRegistryBuilder registry)
{
    registry.HasSequence<long>("order_number_seq", schema: "sales")
        .StartsAt(1000)
        .IncrementsBy(1)
        .HasMin(1000)
        .HasMax(999_999_999)
        .IsCyclic(false);
}
```

Then reference the sequence in a column default:

```csharp
builder.Property(x => x.OrderNumber)
    .HasDefaultValueSql("nextval('sales.order_number_seq')");
```
