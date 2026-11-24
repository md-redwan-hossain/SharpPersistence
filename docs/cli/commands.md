# `db-migrator` CLI Reference

Install as a global or local .NET tool:

```powershell
dotnet tool install -g SharpPersistence.Migrator.Cli
```

## Shared Options

Most commands load your `IServiceProvider` (from `Program.cs`) or `IDesignTimeMigrationFactory`:

| Option | Description |
| --- | --- |
| `-s`, `--startup-project <PATH>` | Startup `.csproj` or directory (auto-detected from CWD if omitted) |
| `-d`, `--destination-project <PATH>` | Project where `MigrationScripts` and `EntityChangelogs` live |
| `-o`, `--output-dir <DIR>` | Override output directory for scripts and snapshots |
| `--no-build` | Skip `dotnet build` before loading the startup assembly |

## Commands

### `doctor`

Validates DI wiring, entity registries, configs, foreign key type compatibility, cross-registry table/index/constraint collisions, and migration script coverage **offline by default**. Pass `--check-db` to also verify live DB connectivity, `__db_migration_logs`, and live schema sync (read-only).

```powershell
# Offline check (CI-safe)
db-migrator doctor --startup-project WebApi/WebApi.csproj

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
  ✓ Migration history table '__db_migration_logs' verified (4 applied, 0 pending)
  ✓ Live database matches your migration scripts

──────────────────────────────────────────────────────────────────────────────
Summary: 9 passed, 0 warnings, 0 errors  (exit code 0)
```

### `diff`

Compares entity configurations against your latest migration scripts and generates safe-to-re-run SQL.

```powershell
db-migrator diff --name Initial --startup-project WebApi/WebApi.csproj
db-migrator diff --name AddOrders --registry SalesRegistry --dry-run
db-migrator diff --name EmptyHook --allow-empty --ci
```

Options: `-n|--name <NAME>` (required), `-r|--registry <REGISTRY>`, `--ci|--non-interactive`, `--allow-empty`, `--dry-run`.

### `migrate`

Applies all pending SQL migration scripts inside a distributed lock (`pg_advisory_lock` / `DBMS_LOCK`).

```powershell
db-migrator migrate --startup-project WebApi/WebApi.csproj
db-migrator migrate --dry-run
db-migrator migrate --sync-ef-history
```

### `status` & `pending`

```powershell
# Table showing APPLIED / PENDING / CHECKSUM MISMATCH
db-migrator status --startup-project WebApi/WebApi.csproj

# List only pending scripts
db-migrator pending --startup-project WebApi/WebApi.csproj
```

### `verify`

Compares live database tables and columns against your C# schema to detect unexpected differences.

```powershell
db-migrator verify --startup-project WebApi/WebApi.csproj
```

### `translate-ef-configs`

Translates EF Core `IEntityTypeConfiguration<T>` C# files into `IEntityConfiguration<T>` files.

```powershell
db-migrator translate-ef-configs Infrastructure/Configurations --output-dir Infrastructure/MigratorConfigs --dry-run
```

Options: `-o|--output-dir <DIR>`, `--no-attribute`, `--dry-run`.

### `translate-ef-migration` & `sync-ef-history`

```powershell
db-migrator translate-ef-migration WebApi/Migrations --startup-project WebApi/WebApi.csproj
db-migrator sync-ef-history --startup-project WebApi/WebApi.csproj --dry-run
```
