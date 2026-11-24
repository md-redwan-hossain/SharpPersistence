# Quick Start: SQL Expressions & Check Constraints

Write dialect-aware SQL check constraints, computed columns, and filter predicates in strongly typed C# instead of fragile raw SQL strings.

## Why Use This?

Hand-written SQL strings like `"\"subtotal\" - COALESCE(\"discount\", 0) > 0"` break silently when you rename a C# property, switch between PostgreSQL and Oracle casing, or forget identifier quotes. `SqlExpressionBuilder<T>` and `SqlCheckConstraintBuilder<T>` validate property names at compile time and format SQL for your target database automatically.

## 1. Install the Package

```powershell
dotnet add package SharpPersistence
```

## 2. Build Expressions in C#

```csharp
using SharpPersistence;
using SharpPersistence.Enums;

var cc = new SqlCheckConstraintBuilder<Order>
{
    Dialect = SqlDialect.PostgreSql,
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
    DelimitIdentifiers = true
};

// Implicitly converts to string—no .Build() needed
string amountCheck = cc.Column(x => x.Subtotal)
    .Math()
    .Minus(cc.Column(x => x.Discount).Coalesce(0m).Math())
    .GreaterThan(0m);
// -> ("subtotal" - COALESCE("discount", 0) > 0)

string shippedCheck = cc.If(cc.Column(x => x.Status).EqualTo("Shipped"))
    .Then(cc.Column(x => x.ShippedAt).IsNotNull());
// -> (NOT ("status" = 'Shipped') OR "shipped_at" IS NOT NULL)
```

## 3. Use with EF Core or SharpPersistence.Migrator

```csharp
// Inside EF Core OnModelCreating / IEntityTypeConfiguration<Order>
builder.ToTable("orders", t =>
{
    t.HasCheckConstraint("ck_orders_net_total", amountCheck);
    t.HasCheckConstraint("ck_orders_shipped_at", shippedCheck);
});

// Inside SharpPersistence.Migrator (Dialect & IdentifierCase are automatic)
builder.HasCheckConstraint("ck_orders_net_total",
    c => c.Column(x => x.Subtotal).Math().Minus(c.Column(x => x.Discount).Coalesce(0m).Math()).GreaterThan(0m));
```

Next: [SQL Expression & Check Builders Reference](/core/check-constraint-builder)
