# SQL Check Constraint Builder

`SqlCheckConstraintBuilder` (and the entity-scoped `SqlCheckConstraintBuilder<TEntity>`) in `SharpPersistence` generates type-safe, dialect-aware SQL `CHECK` constraint expressions across **PostgreSQL**, **Oracle**, **SQL Server**, **MySQL**, and **SQLite**.

Every predicate and built expression implicitly converts to `string`—no `.Build()` call is required.

## Setup

::: code-group

```csharp [Entity-Scoped (Recommended)]
using SharpPersistence;
using SharpPersistence.Enums;

var cc = new SqlCheckConstraintBuilder<Order>
{
    Dialect = SqlDialect.PostgreSql,
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
    DelimitIdentifiers = true,
    NestedSeparator = "_"
};

string sql = cc.Column(x => x.Total).GreaterThan(0m);
// -> ("total" > 0)
```

```csharp [Multi-Entity]
var cc = new SqlCheckConstraintBuilder
{
    Dialect = SqlDialect.Oracle,
    IdentifierCase = SqlIdentifierCase.UpperSnakeCase,
    DelimitIdentifiers = true
};

var order = cc.For<Order>();
string sql = order.Column(x => x.Total).GreaterThan(0m);
// -> ("TOTAL" > 0)
```

:::

## Column Selection

* **`Column(x => x.Prop)`** — Single-level property (throws `ArgumentException` on nested chains).
* **`NestedColumn(x => x.ShippingAddress.Country)`** — Owned/nested properties joined with `NestedSeparator` and formatted with `IdentifierCase`.
* **`RawColumn<TProp>("col_name")`** — Shadow or custom-named columns. Preserves exact casing while respecting `DelimitIdentifiers` (or `.WithDelimiter()` / `.WithoutDelimiter()`).

## Fluent Operations

### Comparisons & Null Checks

Supports values, same-entity column lambdas, `SqlColumnBuilder`, and `SqlBuiltExpression`:

* `EqualTo`, `NotEqualTo` (`EqualTo(null)` / `NotEqualTo(null)` emit `IS NULL` / `IS NOT NULL`)
* `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`
* `Between`, `NotBetween`, `In`, `NotIn`, `IsNull`, `IsNotNull`
* `*RawColumn` / `*RawColumns` when comparing against a raw column name string

```csharp
cc.Column(x => x.EndDate).GreaterThanOrEqual(x => x.StartDate);
cc.Column(x => x.Discount).Between(0m, 100m);
cc.NestedColumn(x => x.ShippingAddress.Country).Length().In(2, 3);
cc.RawColumn<Guid>("tenant_id").NotEqualTo(Guid.Empty);
```

### String Helpers

Available on `string` columns: `.Length()`, `.Trim()`, `.Lower()`, `.Upper()`, `.Like(pattern)`, `.NotLike(pattern)`.

```csharp
// PostgreSQL: reject blank strings
cc.Column(x => x.Code).Trim().Length().GreaterThan(0);

// Oracle: '' is NULL, so check IsNotNull() after Trim()
cc.Column(x => x.Code).Trim().IsNotNull();
```

### Arithmetic & Coalesce

Available on `INumber<T>` columns via `.Math()` (`Plus`, `Minus`, `MultiplyBy`, `DivideBy`, `Modulo`, `Abs`) and `.Coalesce(...)`:

```csharp
cc.Column(x => x.Subtotal)
    .Math()
    .Minus(cc.Column(x => x.Discount).Coalesce(0m).Math())
    .GreaterThan(0m);
```

### Enum & Boolean Converters

```csharp
cc.Column(x => x.Status).EnumAsString().In(OrderStatus.Draft, OrderStatus.Confirmed);
cc.Column(x => x.Status).EnumAsNumeric().Between(1, 3);
cc.Column(x => x.IsPriority).BoolAsNumeric().EqualTo(1);
cc.Column(x => x.IsActive).BoolAsString().In("Y", "N");
```

### Logical Combinators, `If().Then()`, `Case()`, & `RawPredicate`

```csharp
// Implication: (NOT (status = 'Shipped') OR shipped_at IS NOT NULL)
cc.If(cc.Column(x => x.Status).EqualTo("Shipped"))
  .Then(cc.Column(x => x.ShippedAt).IsNotNull());

// Combining rules
cc.And(
    cc.If(cc.Column(x => x.Status).EqualTo("Shipped"))
      .Then(cc.Column(x => x.ShippedAt).IsNotNull()),
    cc.If(cc.Column(x => x.Status).EqualTo("Cancelled"))
      .Then(cc.Column(x => x.CancelledAt).IsNotNull())
);

// CASE expression (with SqlBuiltExpression comparisons)
cc.Case()
    .When(cc.Column(x => x.Total).GreaterThan(100m))
    .Then(cc.Column(x => x.Discount).LessThanOrEqual(50m))
    .EndWithElse(cc.Column(x => x.Discount).LessThanOrEqual(10m));

// Column-level raw predicates (postfix or format lambda)
cc.Column(x => x.Code).RawPredicate("~ '^ORD-[0-9]+$'");
cc.Column(x => x.Code).RawPredicate(col => $"REGEXP_LIKE({col}, '^ORD-[0-9]+$')");
```

::: tip Using inside SharpPersistence.Migrator
When configuring entities with `SharpPersistence.Migrator`, pass a lambda directly to `builder.HasCheckConstraint("ck_name", cc => ...)`—the migrator configures `Dialect` and `IdentifierCase` for you automatically.
:::
