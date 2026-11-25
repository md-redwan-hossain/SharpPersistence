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
// Non-generic (multi-entity)
var cc = new SqlCheckConstraintBuilder
{
    Dialect = SqlDialect.PostgreSql,            // PostgreSql | MySql | SqlServer | Oracle | Sqlite
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase, // LowerSnakeCase | UpperSnakeCase | PascalCase | CamelCase | LowerCase | UpperCase
    DelimitIdentifiers = true,                  // required
    NestedSeparator = "_"                       // optional, default "_"
};

// Entity-scoped generic (single entity — combines cc + cc.For<Order>() into one object)
var orderCc = new SqlCheckConstraintBuilder<Order>
{
    Dialect = SqlDialect.PostgreSql,
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
    DelimitIdentifiers = true
};
```

Every predicate/built expression implicitly converts to `string` (`HasCheckConstraint("name", expr)` or `string sql = expr;`)—there is no `.Build()` method.

## Column Selection Rules

- **Avoid lambda parameter shadowing:** When nesting inside `builder.ToTable(t => ...)`, use a distinct parameter name in `Column(...)` (e.g. `t` for the table builder and `x` or `e` for the entity) instead of reusing `x` in both lambdas.

- **`order.Column(x => x.Prop)`**: Single-level properties only (throws `ArgumentException` on nested chains).
- **`order.NestedColumn(x => x.Address.Country)`**: Owned/nested properties, joined with `NestedSeparator` and transformed by `IdentifierCase`.
- **`cc.RawColumn<TProp>("col_name")`**: Shadow or custom-named DB columns. **Ignores `IdentifierCase`** (preserves exact casing; still respects `DelimitIdentifiers` / `.WithDelimiter()` / `.WithoutDelimiter()`).

## Fluent Operations

- **Comparisons (value, lambda/column, or `SqlBuiltExpression`):** `EqualTo`, `NotEqualTo`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `NotBetween`, `In`, `NotIn`, `IsNull`, `IsNotNull`.
  - `EqualTo(null)` / `NotEqualTo(null)` render `IS NULL` / `IS NOT NULL`.
  - Use `*RawColumn` / `*RawColumns` when comparing against a raw string column name (`EqualToRawColumn("usr_Email")`).
  - `SqlBuiltExpression` (from `Case()` or `Raw(...)`) also supports `EqualTo`, `NotEqualTo`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `IsNull`, and `IsNotNull`, returning a `SqlPredicate`.
- **Column-level raw predicates:**
  - `col.RawPredicate("~ '^ORD-[0-9]+$'")` — appends a postfix/infix raw condition after the rendered column (`"code" ~ '^ORD-[0-9]+$'`).
  - `col.RawPredicate(c => $"REGEXP_LIKE({c}, '^ORD-[0-9]+$')")` — formats a function-wrapped or multi-reference raw predicate using the rendered column without magic placeholders.
- **String (`string` columns only):** `.Length()` (returns comparisons only—no math), `.Trim()`, `.Lower()`, `.Upper()`, `.Like(pattern)`, `.NotLike(pattern)`.
  - Reject whitespace: `col.Trim().Length().GreaterThan(0)` (for **Oracle**, use `col.Trim().IsNotNull()` because Oracle treats `''` as `NULL`).
- **Arithmetic (`INumber<T>` / `INumber<T>?` columns only):** `.Math()` unlocks `.Plus(...)`, `.Minus(...)`, `.MultiplyBy(...)`, `.DivideBy(...)`, `.Modulo(...)`, and `.Abs()`, plus numeric comparisons.
- **Coalesce:** `.Coalesce(fallbackValue)` or `.Coalesce(x => x.FallbackColumn)`.
- **Value-converter helpers:**
  - Enums: `.EnumAsString()` (`'Draft'`) / `.EnumAsNumeric()` (`1`, default).
  - Booleans: `.BoolAsNumeric()` (re-types `bool` to `int` for `.EqualTo(1)` / `.EqualTo(0)`) / `.BoolAsString()` (re-types `bool` to `string` for `.EqualTo("Y")`). Note: `SqlDialect.SqlServer` emits `1`/`0` for `bool` by default.
- **Logical, Conditional & `CASE`:** `cc.And(...)`, `cc.Or(...)`, `.And(...)`, `.Or(...)`, `.Not()`, `cc.If(condition).Then(requirement)` (renders `(NOT (condition) OR requirement)`, works in all dialects including Oracle), `cc.RawPredicate("...")`, and `cc.Case().When(predicate).Then(then).EndWithElse(elseExpr)` / `.End()`.

## Examples

```csharp
// EF Core HasCheckConstraint (implicit string conversion, no .Build())
var order = cc.For<Order>();

builder.ToTable(t =>
{
    t.HasCheckConstraint("ck_order_country",
        order.NestedColumn(x => x.ShippingAddress.Country).Length().In(2, 3));

    t.HasCheckConstraint("ck_order_net_total",
        order.Column(x => x.Subtotal).Math().Minus(order.Column(x => x.Discount).Coalesce(0m).Math()).GreaterThan(0));

    t.HasCheckConstraint("ck_order_status",
        order.Column(x => x.Status).EnumAsString().In(OrderStatus.Draft, OrderStatus.Confirmed));

    t.HasCheckConstraint("ck_order_priority",
        order.Column(x => x.IsPriority).BoolAsNumeric().EqualTo(1));

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

    // Multi-branch CASE (for PostgreSQL/MySQL; for Oracle prefer cc.If(...).Then(...))
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

## Using with `SharpPersistence.Migrator`

`EntityBuilder<TEntity>.HasCheckConstraint`, `IndexBuilder<TEntity>.HasFilter`, and `PostgresExclusionConstraintBuilder<TEntity>.HasFilter` accept `Func<ISqlPredicateBuilder<TEntity>, SqlPredicate>`, and `ColumnBuilder<TEntity>.HasComputedColumnSql` accepts `Func<ISqlExpressionBuilder<TEntity>, SqlBuiltExpression>` (all backed by `SqlExpressionBuilder<TEntity>`, inheriting `Dialect` and `IdentifierCase` automatically). For full migrator setup, registries, and CLI usage, see the `sharp-persistence-migrator` skill.

```csharp
// Global defaults in AddDbMigrator (defaults: DelimitIdentifiers = true, NestedColumnSeparator = "_")
options.ConfigureSqlExpressionBuilder(cc => cc
    .UseDelimitedIdentifiers(false)
    .UseNestedColumnSeparator("__"));

// Entity configuration
builder.HasCheckConstraint(
    "ck_orders_discount",
    cc => cc.Column(x => x.Discount).LessThanOrEqual(
        cc.Case()
            .When(cc.Column(x => x.IsVip).EqualTo(true)).Then(50m)
            .EndWithElse(10m)));

builder.HasCheckConstraint(
        "ck_orders_shipping_zip",
        cc => cc.NestedColumn(x => x.Shipping.ZipCode).IsNotNull())
    .UseDelimitedIdentifiers(true)
    .UseNestedColumnSeparator("__");

builder.HasCheckConstraint(
    "ck_orders_code",
    cc => cc.Column(x => x.Code).RawPredicate("~ '^ORD-[0-9]+$'"));
```

## Migrating from `SqlCheckConstraintGenerator`

| Legacy (`SqlCheckConstraintGenerator`) | New (`SqlCheckConstraintBuilder`) |
| :--- | :--- |
| `new SqlCheckConstraintGenerator(Rdbms.PostgreSql, SqlNamingConvention.LowerSnakeCase, delimitString: false)` | `new SqlCheckConstraintBuilder { Dialect = SqlDialect.PostgreSql, IdentifierCase = SqlIdentifierCase.LowerSnakeCase, DelimitIdentifiers = false }` |
| `cc.EqualTo(nameof(T.Prop), "val", SqlOperandType.Value)` | `entity.Column(x => x.Prop).EqualTo("val")` |
| `cc.EqualTo(nameof(T.A), nameof(T.B), SqlOperandType.Column)` | `entity.Column(x => x.A).EqualTo(x => x.B)` |
| `cc.GreaterThan(nameof(T.Name), 3, SqlDataType.VarChar)` | `entity.Column(x => x.Name).Length().GreaterThan(3)` |
| `cc.EqualTo(nameof(T.Flag), 1, SqlDataType.Int)` *(on `bool` with `0`/`1` converter)* | `entity.Column(x => x.Flag).BoolAsNumeric().EqualTo(1)` |
| `cc.EqualTo("col", true, useIsOperator: true)` | `entity.Column(x => x.Col).EqualTo(true)` *(or `cc.RawPredicate("col IS TRUE")`)* |
| `cc.Math([(nameof(T.A), SqlMathOperator.Add), (nameof(T.B), null)], SqlComparisonOperator.Equal, 50)` | `entity.Column(x => x.A).Math().Plus(x => x.B).EqualTo(50)` |
| `cc.Case().When(cond, then).EndWithElse(1)` | `cc.If(cond).Then(then)` *(preferred for conditional rules, Oracle-safe)* or `cc.Case().When(cond).Then(then).EndWithElse(1)` |
| `delimitLeftOperand: false` / `delimitColumnName: true` | `.WithoutDelimiter()` / `.WithDelimiter()` |

## SQL File Auto-Copy & Generated `IParsedSqlStorage` Extensions

`SharpPersistence` (`2.1.0+`) automatically copies non-migration `.sql` files to `sharp_persistence_sql_files/<AssemblyName>/` in the build/publish output and runs `SqlStorageExtensionGenerator` to emit strongly typed `Get<PascalTag>Sql()` extension methods on `IParsedSqlStorage`:

```sql
-- Queries/Orders.sql
-- #start# get_active_orders
SELECT id, total FROM orders WHERE status = 'active';
-- #end#
```

```csharp
// Compile-time generated extension method (no magic strings):
string sql = parsedSqlStorage.GetActiveOrdersSql();
```

- **Diagnostics (`SPSQL001`–`SPSQL006`):** Flags empty/unclosed blocks, duplicate `#end#`, duplicate tags across files, and unknown constant string tags passed to `IParsedSqlStorage[string]` / `TryGetParsedSql(...)` (`SPSQL006`, including cross-assembly tag checks).
- **MSBuild opt-outs:** Set `<SharpPersistenceAutoCopySqlFiles>false</SharpPersistenceAutoCopySqlFiles>` or `<SharpPersistenceGenerateSqlExtensions>false</SharpPersistenceGenerateSqlExtensions>` in your `.csproj` if needed.
