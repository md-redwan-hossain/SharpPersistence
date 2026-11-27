# `db-migrator` CLI Reference

Install as a global or local .NET tool:

```powershell
dotnet tool install -g SharpPersistence.Migrator.Cli
```

## Shared Options

Most commands load your `IServiceProvider` (from `Program.cs`) or `IDesignTimeDbMigratorFactory`:

| Option | Description |
| --- | --- |
| `-s`, `--startup-project <PATH>` | Startup `.csproj` or directory (auto-detected from CWD if omitted) |
| `-d`, `--destination-project <PATH>` | Project where `DbMigrator/Scripts` and `DbMigrator/Snapshots` live |
| `--no-build` | Skip `dotnet build` before loading the startup assembly |
| `--ci`, `--non-interactive` | Disable interactive prompts (project pickers and rename prompts) |

## Commands

### `config`

Scans your solution (`.slnx` / `.sln`) and generates, inspects, or updates `db-migrator-config.json` at the solution root so you don't need to pass `-s` / `-d` on every command.

```powershell
# Generate or refresh db-migrator-config.json at the solution root
db-migrator config --generate

# Update the default startup project or inspect the current config
db-migrator config --set-default-startup src/WebApi/WebApi.csproj
db-migrator config --show
```

```json
{
  "defaultStartupProject": "src/WebApi/WebApi.csproj",
  "defaultDestinationProject": "src/Infrastructure/Infrastructure.csproj",
  "startupProjects": [
    "src/WebApi/WebApi.csproj"
  ],
  "projects": [
    "src/Infrastructure/Infrastructure.csproj"
  ],
  "otherProjects": []
}
```

- When `defaultStartupProject` or `defaultDestinationProject` is omitted and multiple projects exist, CLI commands open a searchable interactive picker (pass `--ci` in CI pipelines to fail fast instead of prompting).

### `ui`

Starts a local browser UI (`http://127.0.0.1:5077`) backed by your compiled registries without running your Web API host or touching the database until you click **Run Live DB Check**.

```powershell
# Launch and open browser automatically
db-migrator ui

# Custom port, without auto-opening a browser tab
db-migrator ui --port 5099 --no-open --startup-project WebApi/WebApi.csproj
```

Includes 5 views plus `Ctrl/Cmd + K` global search across entities, columns, indexes, foreign keys, and constraints:
- **Entities**: Tables, columns, index counts, foreign keys (`1:1` / `N:1`, required/optional), indexes (ordering, operators, included columns, filters, storage), and check/exclusion constraints.
- **ERD**: Interactive table-level relationship graph with pan/zoom and per-registry filtering.
- **Dependencies**: Topological registry dependency graph, cross-registry FK inspector, and `ReplaceEntityTypes` mappings.
- **Migrations**: **Runlist** (effective execution order + syntax-highlighted SQL) and **Dry Run** (read-only pending schema diff vs. `DbMigrator/Snapshots/*.json`).
- **Doctor**: Offline wiring/schema checks plus on-demand **Run Live DB Check**.

### `doctor`

Validates DI wiring, entity registries, configs, foreign key type compatibility, cross-registry table/index/constraint collisions, entity type replacements, and migration script coverage **offline by default**. Pass `--check-db` to also verify live DB connectivity, `__db_migrator_logs`, database user permissions across all persisted scripts (applied and pending), and live table/column/nullability/SQL-type drift (read-only).

```powershell
# Offline check (CI-safe)
db-migrator doctor --startup-project WebApi/WebApi.csproj

# Focused runlist, header ID, and SQL DDL dependency check
db-migrator doctor --runlist --startup-project WebApi/WebApi.csproj

# Single registry + live DB check
db-migrator doctor --registry SalesRegistry --check-db --startup-project WebApi/WebApi.csproj
```

Sample output:

```text
db-migrator doctor
Project: WebApi (PostgreSQL)  •  Mode: Live DB  •  Registries: 2

Dependency Injection Configuration:
  ✓ Services loaded from WebApi/WebApi.csproj (Program.cs)
  ✓ Migrator configured (Database: PostgreSQL, Naming: LowerSnakeCase)
  ✓ Database connection string and driver verified
  ✓ 2 registries wired: CoreRegistry, SalesRegistry

Registry: CoreRegistry: (6 entities, 41 columns)
  ✓ All 6 entities, 41 columns, 4 foreign keys, and 5 constraints are valid

Registry: SalesRegistry: (11 entities, 84 columns)
  ✓ All 11 entities, 84 columns, 9 foreign keys, and 7 constraints are valid

Migration Scripts:
  ✓ All entity changes are covered by migration scripts

Live Database:
  ✓ Connected to PostgreSQL 18.0 (db: app_dev, 8 ms)
  ✓ Migration history table '__db_migrator_logs' verified (4 applied, 0 pending)
  ✓ Live database matches your migration scripts

──────────────────────────────────────────────────────────────────────────────
Summary: 9 passed, 0 warnings, 0 errors  (exit code 0)
```

### `generate`

Compares entity configurations against your latest per-registry JSON snapshots in `DbMigrator/Snapshots/` and generates safe-to-re-run SQL scripts under `DbMigrator/Scripts/`. Reports every generated script, snapshot create/update/rename, and `db-migration-runlist.json` update (in both normal and `--dry-run` modes).

- **SQL-first `camelCase` snapshots**: Each registry gets its own `DbMigrator/Snapshots/<Registry>.json` (plus `_global.json` for PostgreSQL extensions/enums) with `snapshotVersion`, `dialect`, SQL DDL state, and `entityName`/`propertyName` anchors for automatic rename detection (omitting C#/EF runtime-only metadata and `null`/`false` noise).
- **Registry rename & cross-registry move safety**: If you rename a registry class, `generate` detects the orphaned snapshot and interactively asks to rename it (or fails fast in `--ci` mode). Moving an entity from one registry to another in the same run aborts before writing any files so data is never dropped and recreated.

```powershell
db-migrator generate --name Initial --startup-project WebApi/WebApi.csproj
db-migrator generate --name AddOrders --registry SalesRegistry --dry-run
db-migrator generate --name EmptyHook --allow-empty --ci
```

Options: `-n|--name <NAME>` (required), `-r|--registry <REGISTRY>`, `--ci|--non-interactive`, `--allow-empty`, `--dry-run`.

### `migrate`

Applies all pending SQL migration scripts inside a distributed lock (`pg_advisory_lock` / `DBMS_LOCK`). Verifies SHA-256 checksums of already-applied scripts first and fails fast if an applied script was edited on disk.

```powershell
db-migrator migrate --startup-project WebApi/WebApi.csproj
db-migrator migrate --dry-run
db-migrator migrate --sync-ef-history
db-migrator migrate --skip-permission-check
```

Before executing statement #1 (or creating `__db_migrator_logs`), `migrate` automatically runs a read-only catalog preflight against the pending scripts for both PostgreSQL and Oracle and aborts with copy-pasteable `GRANT` / `ALTER USER` hints if any required privilege is missing. Pass `--skip-permission-check` to opt out (e.g., in disposable integration-test containers).

#### Migration History Table Columns

Applied migrations are recorded in the history table (default `__db_migrator_logs`, configurable via `UseHistoryTable(tableName, schema)`), keyed by `script_id` so renaming a registry, folder, or script filename never causes re-execution:

| Column | PostgreSQL | Oracle | Description |
| :--- | :--- | :--- | :--- |
| `script_id` *(PK)* | `uuid NOT NULL` | `VARCHAR2(36) NOT NULL` | Unique UUIDv7 script ID from the filename prefix (stored without hyphens in Oracle) |
| `script_name` | `varchar(512) NOT NULL` | `NVARCHAR2(512) NOT NULL` | Relative path under the migrations directory (e.g., `SalesRegistry/019ac0..._20261126062500_initial.sql`) |
| `checksum_sha256` | `varchar(64) NOT NULL` | `VARCHAR2(64) NOT NULL` | Lowercase hex SHA-256 hash of the normalized SQL body (tamper detection) |
| `applied_at` | `timestamp with time zone NOT NULL DEFAULT now()` | `TIMESTAMP(7) WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL` | UTC timestamp recorded by the database |

#### How Checksum Verification (`checksum_sha256`) Works

`SharpPersistence.Migrator` guards against accidentally editing already-applied `.sql` files (which would silently cause fresh environments and existing production databases to diverge):

1. **On first apply (`db-migrator migrate`)**: The migrator strips any leading UTF-8 BOM, normalizes line endings (`\r\n` and `\r` -> `\n` so Windows and Linux Git checkouts produce the same hash), computes the lowercase hex SHA-256 hash of the SQL body, and stores it in `checksum_sha256` alongside `script_id`.
2. **Before any subsequent `db-migrator migrate`**: Every script on disk whose `script_id` already exists in the history table is re-hashed and compared against the stored `checksum_sha256`. If any applied script was modified on disk, `migrate` **aborts immediately before executing any pending scripts** and lists the tampered file(s).
3. **Read-only inspection (`status` & `doctor --check-db`)**: Both run the same comparison without modifying the database and report mismatches as `CHECKSUM MISMATCH` / `Error`.
4. **Resolving a mismatch**: Revert the edits to the already-applied `.sql` file and generate a new migration (`db-migrator generate --name <Name>`) for the new schema change instead.

### Script Naming Convention & `runlist`

Every generated `.sql` script (registry, `_global`, and `_deferred`) follows the uniform naming format:

```text
{guid_without_hyphens}_{yyyyMMddHHmmss}_{lower_snake_name}.sql
```

- **Script ID (`{guid_without_hyphens}`)**: 32-character lowercase hex UUIDv7 (`Guid.CreateVersion7().ToString("N")`) at the start of the filename. Because UUIDv7 is time-ordered, files sort chronologically in Git and File Explorer, and `db-migrator` identifies scripts directly from filenames without opening `.sql` files.
- **Timestamp (`{yyyyMMddHHmmss}`)**: UTC generation timestamp.
- **Normalized name (`{lower_snake_name}`)**: Migration names are normalized to `lower_snake_case` (spaces, hyphens, and PascalCase become `_`; e.g., `"Add Trading Type"` -> `add_trading_type`, `"AddedPat"` -> `added_pat`).
- **Translated EF Core migrations (`_translated/`)**: `{guid_without_hyphens}_{normalized_ef_migration_id}.sql`.

Script execution order is tracked in `DbMigrator/Scripts/db-migration-runlist.json` as an array of 32-character lowercase hex GUIDs (without hyphens). To manually reorder a subset of scripts (such as `--allow-empty` data migrations), add an optional `DbMigrator/Scripts/db-migration-runlist.overrides.json` containing arrays of 2 or more script IDs (supports `//` comments and trailing commas):

```jsonc
[
  [
    "019ac0e8b3a27f1d9c4e5b2a1f8d3c01", // add_note
    "019ac0e8b3a37f1d9c4e5b2a1f8d3c02", // custom_backfill
    "019ac0e8b3a47f1d9c4e5b2a1f8d3c03"  // drop_old_col
  ]
]
```

```powershell
# Print effective execution order (including override flags; optionally pass --recent <N>)
db-migrator runlist show --startup-project WebApi/WebApi.csproj
db-migrator runlist show --recent 10 --startup-project WebApi/WebApi.csproj

# Prefix any unprefixed .sql files with a 32-char UUIDv7 and update db-migration-runlist.json
db-migrator runlist sync --startup-project WebApi/WebApi.csproj
db-migrator runlist sync --prune --dry-run
```

### `status` & `pending`

Inspects applied vs. pending migration scripts against the database history table (`status` also checks applied scripts for SHA-256 checksum mismatches; `pending` lists unapplied scripts read-only without reading `.sql` files).

```powershell
# Table showing APPLIED / PENDING / CHECKSUM MISMATCH
db-migrator status --startup-project WebApi/WebApi.csproj

# List only pending scripts
db-migrator pending --startup-project WebApi/WebApi.csproj
```

### `translate-ef-configs`

Translates EF Core `IEntityTypeConfiguration<T>` C# files into `IEntityConfiguration<T>` files without overwriting the originals. When `--output-path` is omitted, translated files are written into `<source-path>/TranslatedEfConfigs`.

```powershell
db-migrator translate-ef-configs --source-path Infrastructure/Configurations --dry-run
db-migrator translate-ef-configs --source-path Infrastructure/Configurations --output-path Infrastructure/MigratorConfigs
```

Options: `-s|--source-path <DIR>` (required), `-o|--output-path <DIR>` (optional, defaults to `<source-path>/TranslatedEfConfigs`), `--no-attribute`, `--dry-run`.

### `annotate-dbcontext-entities`

Annotates entity classes referenced by an EF Core `DbContext` (`DbSet<T>` properties and `modelBuilder.Entity<T>()` calls) with `[Entity]` (default) or `[Entity<TGroup>]` when `--entity-group-name` is provided.

```powershell
# Built-in ungrouped [Entity]
db-migrator annotate-dbcontext-entities --dbcontext Infrastructure/AppDbContext.cs --search-directory Domain/Entities --dry-run

# Grouped [Entity<SalesGroup>] with its namespace
db-migrator annotate-dbcontext-entities --dbcontext Modules/Sales/Infra/SalesDbContext.cs --search-directory Modules/Sales/Domain/Entities --entity-group-name SalesGroup --entity-group-namespace MyApp.Sales.Domain
```

Options: `--dbcontext <PATH>` (required), `--search-directory <DIR>` (required), `--entity-group-name <NAME>` and `--entity-group-namespace <NAMESPACE>` (optional, must be provided together), `--read-only`, `--dry-run`.

### `translate-ef-migrations` & `sync-ef-history`

`translate-ef-migrations` converts compiled EF Core `Migration` classes in `--source-path` 1-to-1 into idempotent SQL scripts under `DbMigrator/Scripts/_translated/` and appends them to `db-migration-runlist.json`. `sync-ef-history` reads `__EFMigrationsHistory` and records matching `_translated` scripts as already applied in `__db_migration_logs` so existing databases won't re-run historical migrations.

```powershell
db-migrator translate-ef-migrations --source-path WebApi/Migrations --startup-project WebApi/WebApi.csproj
db-migrator sync-ef-history --startup-project WebApi/WebApi.csproj --dry-run
```
