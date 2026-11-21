---
name: sharp-persistence
description: >-
  Generate type-safe, dialect-aware SQL CHECK constraint expressions in C# using
  SharpPersistence's SqlCheckConstraintBuilder across PostgreSQL, SQL Server, MySQL, Oracle, and SQLite.
---

# SharpPersistence Check Constraints (`SqlCheckConstraintBuilder`)

Use `SqlCheckConstraintBuilder` (`using SharpPersistence;` + `using SharpPersistence.Enums;`).
Never use the obsolete `SqlCheckConstraintGenerator`, `Rdbms`, or `SqlNamingConvention`.

## Setup

```csharp
var cc = new SqlCheckConstraintBuilder
{
    Dialect = SqlDialect.PostgreSql,            // PostgreSql | MySql | SqlServer | Oracle | Sqlite
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase, // PascalCase | LowerSnakeCase | UpperSnakeCase
    DelimitIdentifiers = true,                  // optional, default true
    NestedSeparator = "_"                       // optional, default "_"
};

var order = cc.For<Order>();
```

Every predicate/built expression implicitly converts to `string` (`HasCheckConstraint("name", expr)` or `string sql = expr;`)—there is no `.Build()` method.

## Column Selection Rules

- **`order.Column(x => x.Prop)`**: Single-level properties only (throws `ArgumentException` on nested chains).
- **`order.NestedColumn(x => x.Address.Country)`**: Owned/nested properties, joined with `NestedSeparator` and transformed by `IdentifierCase`.
- **`cc.RawColumn<TProp>("col_name")`**: Shadow or custom-named DB columns. **Ignores `IdentifierCase`** (preserves exact casing; still respects `DelimitIdentifiers` / `.WithDelimiter()` / `.WithoutDelimiter()`).

## Fluent Operations

- **Comparisons (value or lambda/column):** `EqualTo`, `NotEqualTo`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `NotBetween`, `In`, `NotIn`, `IsNull`, `IsNotNull`.
  - `EqualTo(null)` / `NotEqualTo(null)` render `IS NULL` / `IS NOT NULL`.
  - Use `*RawColumn` / `*RawColumns` when comparing against a raw string column name (`EqualToRawColumn("usr_Email")`).
- **String (`string` columns only):** `.Length()` (returns comparisons only—no math), `.Trim()`, `.Lower()`, `.Upper()`, `.Like(pattern)`, `.NotLike(pattern)`.
  - Reject whitespace: `col.Trim().Length().GreaterThan(0)` (for **Oracle**, use `col.Trim().IsNotNull()` because Oracle treats `''` as `NULL`).
- **Arithmetic (`INumber<T>` / `INumber<T>?` columns only):** `.Math()` unlocks `.Plus(...)`, `.Minus(...)`, `.MultiplyBy(...)`, `.DivideBy(...)`, `.Modulo(...)`, and `.Abs()`, plus numeric comparisons.
- **Coalesce:** `.Coalesce(fallbackValue)` or `.Coalesce(x => x.FallbackColumn)`.
- **Value-converter helpers:**
  - Enums: `.EnumAsString()` (`'Draft'`) / `.EnumAsNumeric()` (`1`, default).
  - Booleans: `.BoolAsNumeric()` (`1`/`0`) / `.BoolAsString("Y", "N")` (`'Y'`/`'N'`). Note: `SqlDialect.SqlServer` emits `1`/`0` for `bool` by default.
- **Logical, Conditional & `CASE`:** `cc.And(...)`, `cc.Or(...)`, `.And(...)`, `.Or(...)`, `.Not()`, `cc.If(condition).Then(requirement)` (renders `(NOT (condition) OR requirement)`, works in all dialects including Oracle 19c), `cc.RawPredicate("...")`, and `cc.Case().When(predicate).Then(then).EndWithElse(elseExpr)` / `.End()`.

## Examples

```csharp
// EF Core HasCheckConstraint (implicit string conversion, no .Build())
builder.ToTable(t =>
{
    t.HasCheckConstraint("ck_order_country",
        order.NestedColumn(x => x.ShippingAddress.Country).Length().In(2, 3));

    t.HasCheckConstraint("ck_order_net_total",
        order.Column(x => x.Subtotal).Math().Minus(order.Column(x => x.Discount).Coalesce(0m).Math()).GreaterThan(0));

    t.HasCheckConstraint("ck_order_status",
        order.Column(x => x.Status).EnumAsString().In(OrderStatus.Draft, OrderStatus.Confirmed));

    t.HasCheckConstraint("ck_order_priority",
        order.Column(x => x.IsPriority).BoolAsNumeric().EqualTo(true));

    t.HasCheckConstraint(
        "ck_order_shipped_at",
        cc.If(order.Column(x => x.Status).EqualTo(OrderStatus.Shipped))
          .Then(order.Column(x => x.ShippedAt).IsNotNull())
    );

    t.HasCheckConstraint(
        "ck_order_status_timestamps",
        cc.And(
            cc.If(order.Column(x => x.Status).EqualTo(OrderStatus.Shipped))
              .Then(order.Column(x => x.ShippedAt).IsNotNull()),
            cc.If(order.Column(x => x.Status).EqualTo(OrderStatus.Cancelled))
              .Then(order.Column(x => x.CancelledAt).IsNotNull())
        )
    );

    // Multi-branch CASE (for PostgreSQL/MySQL; for Oracle 19c prefer cc.If(...).Then(...))
    t.HasCheckConstraint(
        "ck_order_discount_tier",
        cc.Case()
            .When(order.Column(x => x.Total).GreaterThan(100m))
            .Then(order.Column(x => x.Discount).LessThanOrEqual(50m))
            .EndWithElse(order.Column(x => x.Discount).LessThanOrEqual(10m))
    );

    t.HasCheckConstraint("ck_order_tenant",
        cc.RawColumn<Guid>("tenant_id").NotEqualTo(Guid.Empty));
});
```

## Migrating from `SqlCheckConstraintGenerator`

| Legacy (`SqlCheckConstraintGenerator`) | New (`SqlCheckConstraintBuilder`) |
| :--- | :--- |
| `new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase, delimitString: false)` | `new SqlCheckConstraintBuilder { Dialect = SqlDialect.PostgreSql, IdentifierCase = SqlIdentifierCase.LowerSnakeCase, DelimitIdentifiers = false }` |
| `cc.EqualTo(nameof(T.Prop), "val", SqlOperandType.Value)` | `entity.Column(x => x.Prop).EqualTo("val")` |
| `cc.EqualTo(nameof(T.A), nameof(T.B), SqlOperandType.Column)` | `entity.Column(x => x.A).EqualTo(x => x.B)` |
| `cc.GreaterThan(nameof(T.Name), 3, SqlDataType.VarChar)` | `entity.Column(x => x.Name).Length().GreaterThan(3)` |
| `cc.EqualTo(nameof(T.Flag), 1, SqlDataType.Int)` *(on `bool` with `0`/`1` converter)* | `entity.Column(x => x.Flag).BoolAsNumeric().EqualTo(true)` |
| `cc.EqualTo("col", true, useIsOperator: true)` | `entity.Column(x => x.Col).EqualTo(true)` *(or `cc.RawPredicate("col IS TRUE")`)* |
| `cc.Math([(nameof(T.A), SqlMathOperator.Add), (nameof(T.B), null)], SqlComparisonOperator.Equal, 50)` | `entity.Column(x => x.A).Math().Plus(x => x.B).EqualTo(50)` |
| `cc.Case().When(cond, then).EndWithElse(1)` | `cc.If(cond).Then(then)` *(preferred for conditional rules, Oracle 19c-safe)* or `cc.Case().When(cond).Then(then).EndWithElse(1)` |
| `delimitLeftOperand: false` / `delimitColumnName: true` | `.WithoutDelimiter()` / `.WithDelimiter()` |
