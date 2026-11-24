# SQL Parser & `IParsedSqlStorage`

`SqlParser` (in `SharpPersistence`) and `IParsedSqlStorage` (in `SharpPersistence.Abstractions`) let you keep complex hand-written SQL queries in real `.sql` files—with full IDE SQL syntax highlighting—and load them safely by tag at runtime.

## Why Use This?

Embedding long multi-line SQL strings inside C# files (`"""SELECT ..."""`) makes queries hard to read, format, and test in a SQL client. Putting them in `.sql` files with `--@tag` markers lets you edit and run them directly in DataGrip, pgAdmin, or SQL Developer, while `SqlParser` validates every tag once at startup and exposes them through `IParsedSqlStorage`.

## 1. Write Tagged `.sql` Files

Place your `.sql` files in a folder (default: `sharp_persistence_sql_files`, copied to output directory) and tag each query block:

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
