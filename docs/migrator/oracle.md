# Oracle Features

`SharpPersistence.Migrator` generates idempotent Oracle DDL and handles Oracle-specific limits, PL/SQL blocks, and locking automatically.

## Setup & Compatibility Level

```csharp
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;

builder.Services.AddDbMigrator(opt =>
{
    opt.UseOracle(connectionString)
        .UseSqlCompatibility(OracleDbCompatibility.Oracle23); // or Oracle19 (default)

    opt.AddGeneratedEntityRegistries();
});
```

* **`OracleDbCompatibility.Oracle19`** *(default)* — Wraps DDL in `BEGIN EXECUTE IMMEDIATE '...'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 ... END; /` PL/SQL blocks for safe re-runs.
* **`OracleDbCompatibility.Oracle23`** — Uses native `IF NOT EXISTS` / `IF EXISTS` syntax supported in Oracle 23ai.

## Identifier Casing & 30/128-Byte Truncation

`UseOracle(...)` defaults `IdentifierCase` to `SqlIdentifierCase.UpperSnakeCase` and automatically truncates long constraint/index identifiers (while appending a deterministic hash suffix) so Oracle never rejects long generated names.

## Unicode & String Types

```csharp
// Default: NVARCHAR2(100) / NCLOB
builder.Property(x => x.DisplayName).HasMaxLength(100);

// Non-Unicode: VARCHAR2(50) / CLOB
builder.Property(x => x.IsoCode).HasMaxLength(50).IsUnicode(false);
```

## Concurrency & Execution

* **Distributed Lock:** `MigrateAsync` acquires an Oracle `DBMS_LOCK` (`lockmode => 6`, `release_on_commit => FALSE`) so concurrent instances wait safely.
* **Statement Splitting:** `OracleMigrationDialect` splits SQL statements and PL/SQL `/` blocks automatically during `MigrateAsync`—including `BEGIN ... END; /` blocks.
* **Unsupported PostgreSQL Features:** Features without an Oracle equivalent (like `HasExclusionConstraint`) fail fast with a clear error during `db-migrator doctor` and `generate`.
