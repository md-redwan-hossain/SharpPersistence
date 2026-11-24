# SQL Parser & `IParsedSqlStorage`

`SqlParser` (in `SharpPersistence`) and `IParsedSqlStorage` (in `SharpPersistence.Abstractions`) let you keep complex hand-written SQL queries in real `.sql` files—with full IDE SQL syntax highlighting—and load them safely by tag at runtime.

## Why Use This?

Embedding long multi-line SQL strings inside C# files (`"""SELECT ..."""`) makes queries hard to read, format, and test in a SQL client. Putting them in `.sql` files with `--@tag` markers lets you edit and run them directly in DataGrip, pgAdmin, or SQL Developer, while `SqlParser` validates every tag once at startup and exposes them through `IParsedSqlStorage`.

## 1. Write Tagged `.sql` Files

Place your `.sql` files anywhere in your project (or in any referenced class library) and tag each query block:

```sql
--@get_monthly_revenue
SELECT
    date_trunc('month', created_at) AS month,
    SUM(total) AS revenue
FROM sales.orders
WHERE status = 'paid'
GROUP BY 1
ORDER BY 1 DESC;

--@get_customer_balance
SELECT COALESCE(SUM(amount), 0)
FROM billing.ledger_entries
WHERE customer_id = @customer_id;
```

### Automatic Build & Publish Copying

`SharpPersistence` ships an MSBuild `buildTransitive` target that automatically copies `.sql` files from every project in your solution that directly or transitively references the package into:

```text
sharp_persistence_sql_files/<AssemblyName>/<RelativePath>/<File>.sql
```

You do **not** need to configure `Directory.Build.props` or set `CopyToOutputDirectory` on individual `.sql` files.

- **Migrator coexistence:** `MigrationScripts/` and any folder containing `db-migration-runlist.json` are automatically excluded so migration scripts are not copied into `sharp_persistence_sql_files/`.
- **Embedded resources:** `.sql` files explicitly marked as `<EmbeddedResource>` are left untouched.
- **Customization / opt-out:** Set MSBuild properties in your `.csproj` or `Directory.Build.props` if you need to override the defaults:

```xml
<PropertyGroup>
  <!-- Disable automatic .sql copying for a project -->
  <SharpPersistenceAutoCopySqlFiles>false</SharpPersistenceAutoCopySqlFiles>

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

// Scans all *.sql files recursively under the directory
IParsedSqlStorage sqlStorage = parser.ParseFromStorage(
    directoryName: "SqlQueries",
    throwIfDirectoryNotExists: true);

builder.Services.AddSingleton(sqlStorage);
```

You can also parse SQL directly from a string (useful in unit tests):

```csharp
IParsedSqlStorage storage = new SqlParser().ParseFromString("""
    --@ping
    SELECT 1;
    """);
```

## 3. Look Up Queries by Tag

Inject `IParsedSqlStorage` into your repository or query service:

```csharp
public sealed class ReportingService(IParsedSqlStorage sqlStorage, NpgsqlDataSource dataSource)
{
    public async Task<decimal> GetCustomerBalanceAsync(Guid customerId, CancellationToken ct)
    {
        // Indexer throws if the tag is missing
        string sql = sqlStorage["get_customer_balance"];

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

`IParsedSqlStorage` also implements `IEnumerable<ParsedSql>`, so you can iterate over every loaded query for diagnostics or startup checks.
