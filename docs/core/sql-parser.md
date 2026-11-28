# SQL Parser & `IParsedSqlStorage`

`SqlParser` (in `SharpPersistence`) and `IParsedSqlStorage` (in `SharpPersistence.Abstractions`) let you keep complex hand-written SQL queries in real `.sql` files—with full IDE SQL syntax highlighting—and load them safely by tag at runtime.

## Overview

`SqlParser` validates every `-- #start# <tag>` / `-- #end# <tag>` block once at startup and exposes the parsed queries through `IParsedSqlStorage` and generated strongly-typed extension methods, while keeping your queries in `.sql` files with full IDE syntax highlighting.

## 1. Write Tagged `.sql` Files

Place your `.sql` files anywhere in your project (or in any referenced class library) and wrap each query block with `-- #start# <tag>` and `-- #end# <tag>`:

```sql
-- #start# get_monthly_revenue
SELECT
    date_trunc('month', created_at) AS month,
    SUM(total) AS revenue
FROM sales.orders
WHERE status = 'paid'
GROUP BY 1
ORDER BY 1 DESC;
-- #end# get_monthly_revenue

-- #start# get_customer_balance
SELECT COALESCE(SUM(amount), 0)
FROM billing.ledger_entries
WHERE customer_id = @customer_id;
-- #end# get_customer_balance
```

### Automatic Build & Publish Copying

`SharpPersistence` ships an MSBuild `buildTransitive` target that automatically copies `.sql` files from every project in your solution that directly or transitively references the package into:

```text
sharp_persistence_sql_files/<AssemblyName>/<RelativePath>/<File>.sql
```

You do **not** need to configure `Directory.Build.props` or set `CopyToOutputDirectory` on individual `.sql` files.

- **Migrator coexistence:** `DbMigrator/Scripts/` and any folder containing `db-migration-runlist.json` are automatically excluded so migration scripts are not copied into `sharp_persistence_sql_files/`.
- **Embedded resources:** `.sql` files explicitly marked as `<EmbeddedResource>` are left untouched.
- **Customization / opt-out:** Set MSBuild properties in your `.csproj` or `Directory.Build.props` if you need to override the defaults:

```xml
<PropertyGroup>
  <!-- Disable automatic .sql copying for a project -->
  <SharpPersistenceAutoCopySqlFiles>false</SharpPersistenceAutoCopySqlFiles>

  <!-- Disable compile-time Get<Tag>Sql() extension method generation -->
  <SharpPersistenceGenerateSqlExtensions>false</SharpPersistenceGenerateSqlExtensions>

  <!-- Optional: change output root folder (default: sharp_persistence_sql_files) -->
  <SharpPersistenceSqlOutputFolder>sharp_persistence_sql_files</SharpPersistenceSqlOutputFolder>

  <!-- Optional: exclude additional glob patterns -->
  <SharpPersistenceSqlExclude>Scratch\**\*.sql</SharpPersistenceSqlExclude>
</PropertyGroup>
```

> **Note:** `buildTransitive` targets are automatically imported when consuming `SharpPersistence` as a NuGet `PackageReference`. If you reference the library source via a local `<ProjectReference>`, import `SharpPersistence.targets` manually or keep your own copy rule.

## 2. Parse Once & Register in DI

```csharp
using SharpPersistence.Abstractions;
using SharpPersistence.Parsers;

var parser = new SqlParser();

// Scans all *.sql files recursively under sharp_persistence_sql_files (default)
IParsedSqlStorage sqlStorage = parser.ParseFromStorage(
    throwIfDirectoryNotExists: true);

builder.Services.AddSingleton(sqlStorage);
```

If you customized `<SharpPersistenceSqlOutputFolder>`, pass the matching `directoryName` to `ParseFromStorage(directoryName: "custom_sql_folder", throwIfDirectoryNotExists: true)`.

You can also parse SQL directly from a string (useful in unit tests):

```csharp
IParsedSqlStorage storage = new SqlParser().ParseFromString("""
    -- #start# ping
    SELECT 1;
    -- #end# ping
    """);
```

## 3. Look Up Queries by Tag (Strongly-Typed or String Key)

`SharpPersistence` includes a compile-time source generator that scans your `.sql` files and generates strongly-typed `Get<Tag>Sql()` extension methods on `IParsedSqlStorage` in your project's `RootNamespace`:

- `get_customer_balance` → `sqlStorage.GetCustomerBalanceSql()`
- `monthly_revenue` → `sqlStorage.GetMonthlyRevenueSql()`
- `insert_order` → `sqlStorage.GetInsertOrderSql()`
- `get_balance_sql` → `sqlStorage.GetBalanceSql()` (never duplicates `Get` or `Sql`)

Each generated method delegates directly to `storage["<tag>"]` at runtime—so you get compile-time safety and IDE hover previews **while still keeping `.sql` files on disk for hot-swapping without rebuilding**.

Inject `IParsedSqlStorage` into your repository or query service:

```csharp
public sealed class ReportingService(IParsedSqlStorage sqlStorage, NpgsqlDataSource dataSource)
{
    public async Task<decimal> GetCustomerBalanceAsync(Guid customerId, CancellationToken ct)
    {
        // Compile-time generated extension method (delegates to sqlStorage["get_customer_balance"])
        string sql = sqlStorage.GetCustomerBalanceSql();

        // Or use TryGetParsedSql for optional queries
        if (!sqlStorage.TryGetParsedSql("get_customer_balance", out var optionalSql))
        {
            throw new InvalidOperationException("Query not found.");
        }

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@customer_id", customerId);
        return Convert.ToDecimal(await cmd.ExecuteScalarAsync(ct));
    }
}
```

### Compile-Time Diagnostics

| Rule | Severity | Meaning |
|------|----------|---------|
| `SPSQL001` | Error | Missing tag name or unclosed `-- #start# <tag>` block |
| `SPSQL002` | Error | Missing tag name, orphan `-- #end# <tag>`, or duplicate `-- #end#` |
| `SPSQL003` | Error | Empty SQL block between `#start#` and `#end#` |
| `SPSQL004` | Error | Duplicate SQL tag within a file or across `.sql` files |
| `SPSQL005` | Error | Two different tags normalize to the same `Get<Tag>Sql()` method name |

`IParsedSqlStorage` also implements `IEnumerable<ParsedSql>`, so you can iterate over every loaded query for diagnostics or startup checks.
