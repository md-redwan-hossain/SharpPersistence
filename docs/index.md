---
layout: default
---

| Branch | Status                                                                                                                |
| ------ | --------------------------------------------------------------------------------------------------------------------- |
| main   | ![Dotnet 10](https://github.com/md-redwan-hossain/SharpPersistence/actions/workflows/dotnet.yaml/badge.svg?branch=main) |

# SharpPersistence

SharpPersistence is a collection of robust, extensible, and thoroughly tested .NET libraries for working with database
systems.

### Installation

To install the packages, run the following commands:

```bash
dotnet add package SharpPersistence.Abstractions
dotnet add package SharpPersistence
dotnet add package SharpPersistence.EfCore
```

Or visit:

- [SharpPersistence.Abstractions on NuGet](https://www.nuget.org/packages/SharpPersistence.Abstractions/)

- [SharpPersistence on NuGet](https://www.nuget.org/packages/SharpPersistence/)

- [SharpPersistence.EfCore on NuGet](https://www.nuget.org/packages/SharpPersistence.EfCore/)

## Features

### **`SqlParser`**

- `SqlParser` is available in `SharpPersistence`
- Separation of Concerns: Keep complex SQL queries in `.sql` files instead of cluttering your C# code
- Better SQL Editing: Full syntax highlighting, IntelliSense, and formatting in SQL files
- Version Control Friendly: Track SQL changes separately from business logic
- Team Collaboration: Database developers can work on SQL files while C# developers focus on application logic
- Compile-Time Safety: Parse and validate SQL files at application startup, catching errors early

#### Basic Usage

**1. Create SQL files with tagged blocks:**

- SQL statements must be enclosed by `-- #start# TagName` and `-- #end# TagName` comments.
- Unique tag of a sql statement is case-insensitive.

```sql
-- #start# GetAllUsers
SELECT u.Id, u.Name, u.Email, u.CreatedAt
FROM Users u
WHERE u.Id = :userId
ORDER BY u.Name
-- #end# GetAllUsers

-- #start# GetActiveUsers
SELECT u.Id, u.Name, u.Email
FROM Users u
WHERE u.IsActive = 1 AND u.IsDeleted = 0
ORDER BY u.LastLoginDate DESC
-- #end# GetActiveUsers
```

**2. Configure your project to copy SQL files:**

- Create a file named `Directory.Build.props` at the same level of your `sln` or `slnx` file.
- Copy and paste the following code in the `Directory.Build.props` file.
- If you already have `Directory.Build.props`, then copy the code inside `<Project> </Project>`

```xml

<Project>
    <PropertyGroup>
        <DefaultItemExcludes>
            $(DefaultItemExcludes);out/**;publish/**;bin/**;obj/**
        </DefaultItemExcludes>
    </PropertyGroup>

    <ItemGroup>
        <Content Include="**\*.sql"
                 Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"
                 CopyToOutputDirectory="PreserveNewest"
                 TargetPath="sharp_persistence_sql_files\$(AssemblyName)\%(RecursiveDir)\%(Filename)%(Extension)"/>
    </ItemGroup>
</Project>

```

- Alternatively, if you want to handle it from `csproj` file, Add the following code to your `csproj` file's `<Project> </Project>` node.

```xml

<PropertyGroup>
    <DefaultItemExcludes>
        $(DefaultItemExcludes);out/**;publish/**;bin/**;obj/**
    </DefaultItemExcludes>
</PropertyGroup>

<ItemGroup>
<Content Include="**\*.sql"
         Exclude="$(DefaultItemExcludes);$(DefaultExcludesInProjectFolder)"
         CopyToOutputDirectory="PreserveNewest"
         TargetPath="sharp_persistence_sql_files\$(AssemblyName)\%(RecursiveDir)\%(Filename)%(Extension)"/>
</ItemGroup>
```

- `sharp_persistence_sql_files` is the default directory. If you want to change it, make sure to pass the new value in
  the `ParseFromStorage()` method's parameter. The same value must be used in the `TargetPath` of the above code.

**3. Parse and use in your application:**

```csharp
// Parse SQL files at startup
IParsedSqlStorage parsedSqlStorage = new SqlParser().ParseFromStorage();

// Access parsed SQL by tag name (case-insensitive) through C# indexer
string getAllUsersQuery = parsedSqlStorage["GetAllUsers"];
// Use the query...

// Safe access with TryGet
if (parsedSqlStorage.TryGetParsedSql("GetAllUsers", out string? query))
{
    // Use the query...
}
```

**4. Dependency Injection (ASP.NET Core):**

```csharp
// In Program.cs
IParsedSqlStorage parsedSqlStatements = new SqlParser().ParseFromStorage();
builder.Services.AddSingleton<IParsedSqlStorage>(parsedSqlStatements);

// In your service classes
public class UserService
{
    private readonly IDbConnectionFactory _dbConnectionFactory;
    private readonly IParsedSqlStorage _parsedSqlStorage;

    public UserService(IDbConnectionFactory dbConnectionFactory, IParsedSqlStorage parsedSqlStorage)
    {
        _dbConnectionFactory = dbConnectionFactory;
        _parsedSqlStorage = parsedSqlStorage;
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync(int userId, CancellationToken ct)
    {
        var sql = _parsedSqlStorage["GetAllUsers"];

        await using var connection = await _dbConnectionFactory.CreateConnectionAsync(ct);

        var parameters = new { userId };

        return await connection.QueryAsync<User>(new CommandDefinition(sql, parameters,
            cancellationToken: ct));
    }
}
```

**Notes:**
SqlParser validates your SQL files at startup and throws detailed error messages for:

- Missing start/end tags
- Duplicate tag names
- Invalid tag formats

### **`SqlCheckConstraintBuilder`**

- `SqlCheckConstraintBuilder` is available in `SharpPersistence` (`using SharpPersistence;` + `using SharpPersistence.Enums;`).
- Replaces the obsolete `SqlCheckConstraintGenerator`, `Rdbms`, and `SqlNamingConvention` types.
- Compile-time type safety: `.Math()` is only available on numeric columns (`INumber<T>`), `.Length()` / `.Trim()` / `.Lower()` / `.Upper()` / `.Like()` only on `string` columns, and `Case()` enforces `When -> Then -> End / EndWithElse` ordering.
- Supports PostgreSQL, MySQL, SQL Server, Oracle, and SQLite.
- Supports `PascalCase`, `lower_snake_case`, and `UPPER_SNAKE_CASE` identifier casing.
- Every predicate/built expression implicitly converts to `string` (`HasCheckConstraint("name", expr)` or `string sql = expr;`)—there is no `.Build()` method.

#### Setup

```csharp
var cc = new SqlCheckConstraintBuilder
{
    Dialect = SqlDialect.PostgreSql,                   // PostgreSql | MySql | SqlServer | Oracle | Sqlite
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase, // PascalCase | LowerSnakeCase | UpperSnakeCase
    DelimitIdentifiers = true,                         // required
    NestedSeparator = "_"                              // optional, default "_"
};
```

#### Column Selection & Operations

- **Avoid lambda parameter shadowing:** When nesting inside `builder.ToTable(t => ...)`, use a distinct parameter name in `Column(...)` (e.g., `t` for the table builder and `x` or `e` for the entity) instead of reusing `x` in both lambdas.
- **`entity.Column(x => x.Prop)`**: Single-level properties only (throws `ArgumentException` on nested chains).
- **`entity.NestedColumn(x => x.Address.Country)`**: Owned/nested properties, joined with `NestedSeparator` and transformed by `IdentifierCase`.
- **`cc.RawColumn<TProp>("col_name")`**: Shadow or custom-named DB columns. **Ignores `IdentifierCase`** (preserves exact casing; still respects `DelimitIdentifiers` / `.WithDelimiter()` / `.WithoutDelimiter()`).
- **Comparisons (value or lambda/column):** `EqualTo`, `NotEqualTo`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `Between`, `NotBetween`, `In`, `NotIn`, `IsNull`, `IsNotNull`. `EqualTo(null)` / `NotEqualTo(null)` render `IS NULL` / `IS NOT NULL`.
- **String (`string` columns only):** `.Length()` (comparisons only), `.Trim()`, `.Lower()`, `.Upper()`, `.Like(pattern)`, `.NotLike(pattern)`.
- **Arithmetic (`INumber<T>` / `INumber<T>?` columns only):** `.Math()` unlocks `.Plus(...)`, `.Minus(...)`, `.MultiplyBy(...)`, `.DivideBy(...)`, `.Modulo(...)`, and `.Abs()`.
- **Coalesce & Value Converters:** `.Coalesce(fallbackValue | x => x.FallbackColumn)`, `.EnumAsString()` / `.EnumAsNumeric()`, `.BoolAsNumeric()` (re-types `bool` to `int` for `.EqualTo(1)` / `.EqualTo(0)`), `.BoolAsString()` (re-types `bool` to `string` for `.EqualTo("Y")`).
- **Logical, Conditional & `CASE`:** `cc.And(...)`, `cc.Or(...)`, `.And(...)`, `.Or(...)`, `.Not()`, `cc.If(condition).Then(requirement)` (renders `(NOT (condition) OR requirement)`, works in all dialects including Oracle), `cc.RawPredicate("...")`, and `cc.Case().When(predicate).Then(then).EndWithElse(elseExpr)` / `.End()`.

#### Examples with EF Core `IEntityTypeConfiguration`

```csharp
var cc = new SqlCheckConstraintBuilder
{
    Dialect = SqlDialect.PostgreSql,
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
    DelimitIdentifiers = false
};

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

    // Multi-branch conditional rule via cc.If(...).Then(...) (all dialects including Oracle)
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

You can also use the generic `SqlCheckConstraintBuilder<TEntity>` to avoid bouncing between `cc` and `cc.For<T>()`, attach column-level `RawPredicate` (postfix or formatted lambda), and compare `Case()` / `Raw()` expressions directly:

```csharp
var cc = new SqlCheckConstraintBuilder<Order>
{
    Dialect = SqlDialect.PostgreSql,
    IdentifierCase = SqlIdentifierCase.LowerSnakeCase,
    DelimitIdentifiers = true
};

// Column-level raw predicate (postfix or formatted lambda)
string pgRegex = cc.Column(x => x.Code).RawPredicate("~ '^ORD-[0-9]+$'");
string oracleRegex = cc.Column(x => x.Code).RawPredicate(col => $"REGEXP_LIKE({col}, '^ORD-[0-9]+$')");

// Comparing a column against a CASE expression (or CASE against a literal)
string tieredDiscount = cc.Column(x => x.Discount).LessThanOrEqual(
    cc.Case()
        .When(cc.Column(x => x.IsVip).EqualTo(true)).Then(50m)
        .EndWithElse(10m));

string caseCheck = cc.Case()
    .When(cc.Column(x => x.Status).EqualTo(OrderStatus.Shipped)).Then(1)
    .EndWithElse(0)
    .EqualTo(1);
```

### **`SharpPersistence.Migrator` Check Constraint Integration**

`SharpPersistence.Migrator` integrates directly with `SqlCheckConstraintBuilder<TEntity>`, automatically inheriting the active `SqlDialect` and `SqlIdentifierCase`:

```csharp
// Optional global defaults in DI setup (defaults: DelimitIdentifiers = true, NestedColumnSeparator = "_")
services.AddDbMigrator(options =>
{
    options.UsePostgreSql(connectionString)
           .UseIdentifierCase(SqlIdentifierCase.LowerSnakeCase)
           .ConfigureCheckConstraintBuilder(cc => cc
               .UseDelimitedIdentifiers(true)
               .UseNestedColumnSeparator("_"));
});

// Inside IEntityConfiguration<Order> / EntityBuilder<Order>
builder.HasCheckConstraint(
    "ck_orders_total",
    cc => cc.Column(x => x.Total).GreaterThanOrEqual(0));

builder.HasCheckConstraint(
        "ck_orders_shipping_zip",
        cc => cc.NestedColumn(x => x.Shipping.ZipCode).IsNotNull())
    .UseDelimitedIdentifiers(false)
    .UseNestedColumnSeparator("__");

builder.HasCheckConstraint(
    "ck_orders_discount",
    cc => cc.Column(x => x.Discount).LessThanOrEqual(
        cc.Case()
            .When(cc.Column(x => x.IsVip).EqualTo(true)).Then(50m)
            .EndWithElse(10m)));

builder.HasCheckConstraint(
    "ck_orders_code",
    cc => cc.Column(x => x.Code).RawPredicate("~ '^ORD-[0-9]+$'"));

// Raw SQL overload remains available alongside the typed overload
builder.HasCheckConstraint("ck_orders_custom", "total >= discount");
```

### **`RepositoryCore` with `IRepositoryCore`**

- `IRepositoryCore` is available in `SharpPersistence.Abstractions`
- `RepositoryCore` is available in `SharpPersistence.EfCore`
- Prefer this for all new code: CRUD, tracking (`TrackEntity` / `TrackEntities`), bulk delete (`RemoveManyDirectAsync` / `RemoveManyDirectIgnoringFiltersAsync`), and `Query()` (no `Get*` overload grid)
- `Query()` returns `IEntityQuery<T>` for composable reads (`Where`, `OrderBy`, `OrderByDesc`, `EnableTracking`, `IgnoreQueryFilters`, `OffsetPaginate`, `Select`, `GetFirstOrDefaultAsync`, `GetFirstOrNullAsync`, `GetAllAsync`, `GetCountAsync`, `ExistsAsync`, `EveryAsync`, `SumAsync`, `MinAsync`, `MaxAsync`, `MinOrNullAsync`, `MaxOrNullAsync`)
- Set tracking on the entity query before `Select`; after projection, tracking methods do nothing

```csharp
// Register / inject IRepositoryCore<User> (or your derived interface)
var allData = await repo.Query()
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .OffsetPaginate(page: 1, limit: 20)
    .Select(u => new { u.Id, u.Name })
    .GetAllAsync(ct);

var singleData = await repo.Query()
    .Where(u => u.Id == id)
    .EnableTracking()
    .GetFirstOrDefaultAsync(ct);

bool hasPending = await repo.Query()
    .Where(u => u.IsActive)
    .ExistsAsync(u => u.IsPending, ct);

int deleted = await repo.RemoveManyDirectAsync(u => !u.IsActive, ct);

int purged = await repo.RemoveManyDirectIgnoringFiltersAsync(
    u => u.IsDeleted,
    [AppDbContext.SoftDeleteFilter],
    ct);

// Aggregates (single column or computed expression)
decimal total = await orderRepo.Query()
    .Where(x => x.IsPaid)
    .SumAsync(x => x.Total, ct);

decimal netTotal = await orderRepo.Query()
    .Where(x => x.IsPaid)
    .SumAsync(x => x.Subtotal - x.Discount, ct);

DateTime? latestOrderAt = await orderRepo.Query()
    .Where(x => x.CustomerId == customerId)
    .MaxOrNullAsync(x => x.CreatedAt, ct);
```

### **`RepositoryBase` with `IRepositoryBase` *(Obsolete)***

- Marked `[Obsolete("Use IRepositoryCore<TEntity> / RepositoryCore<TEntity, TDbContext> instead.")]`
- `IRepositoryBase` extends `IRepositoryCore` (same `Query` + CRUD surface, plus legacy `Get*` overloads)
- `RepositoryBase` extends `RepositoryCore` and implements `IRepositoryBase`
- Existing consumers keep working, but new code should use `IRepositoryCore` / `RepositoryCore`

```csharp
// Legacy overload style (obsolete)
var user = await repo.GetOneAsync(u => u.IsActive);

// Recommended fluent query style (via IRepositoryCore)
var page = await repo.Query()
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .OffsetPaginate(page: 1, limit: 20)
    .GetAllAsync(ct);
```

### **`UnitOfWork` with `IUnitOfWork`**

- `IUnitOfWork` is available in `SharpPersistence.Abstractions`
- `UnitOfWork` is available in `SharpPersistence.EfCore`
- Transactional management of multiple repository operations
- Ensures atomicity and consistency across changes
- Integrates seamlessly with the repository pattern
- Simplifies commit/rollback logic in service layers

## Testing

- All features are covered by xUnit v3 and Shouldly assertions.
- Tests cover all overloads, edge cases, and RDBMS/naming convention combinations.
- To run tests:
  ```bash
  dotnet test
  ```

## Contributing

Contributions are welcome! Please open issues or submit pull requests for bug fixes, improvements, or new features.
