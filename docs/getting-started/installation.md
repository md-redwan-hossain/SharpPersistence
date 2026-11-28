# Installation and Packages

`SharpPersistence` targets **.NET 10 (`net10.0`)** and **.NET 11 (`net11.0`)**. The library is distributed as modular NuGet packages so domain, application, and infrastructure layers maintain clean dependency boundaries.

## Package Matrix

| Package | Target Layer | Purpose |
| --- | --- | --- |
| `SharpPersistence.Abstractions` | Domain / Application | Zero-dependency repository and unit-of-work contracts (`IRepositoryCore<T>`, `IUnitOfWork`, `IUnitOfWorkTransaction`) and compile-time entity discovery attributes (`[Entity]`, `[Entity<TGroup>]`, `IEntityGroup`). Bundles `SharpPersistence.Abstractions.Generators.dll`. |
| `SharpPersistence` | Shared / Infrastructure | Dialect-aware `SqlCheckConstraintBuilder`, `SqlParser`, and collection/queryable helpers. |
| `SharpPersistence.EfCore` | Infrastructure | EF Core implementations of `RepositoryCore<T, TDbContext>` and `UnitOfWork<TDbContext>`. |
| `SharpPersistence.Migrator` | Infrastructure / Migrator Console | EF Core–free schema scanner, differ, DDL generators (PostgreSQL and Oracle), distributed locking, and migration runner. Bundles `SharpPersistence.Migrator.Generators.dll` and `SharpPersistence.Migrator.CodeFixes.dll`. |
| `SharpPersistence.Migrator.EfCore` | Infrastructure | `ApplySchemaToEfCore` runtime bridge, `[BindEntityRegistry<TRegistry>]` support, and EF Core `ValueConverter` extensions. |
| `SharpPersistence.Migrator.Cli` | Global / Local .NET Tool | `db-migrator` CLI (`config`, `ui`, `doctor`, `generate`, `migrate`, `runlist`, `status`, `pending`, `annotate-entities`, `translate-ef-configs`, `translate-ef-migrations`, `sync-ef-history`). |

## Install NuGet Packages

::: code-group

```powershell [Domain / Application]
dotnet add package SharpPersistence.Abstractions
```

```powershell [Repositories & Check Constraints]
dotnet add package SharpPersistence
dotnet add package SharpPersistence.EfCore
```

```powershell [Database Migrator]
dotnet add package SharpPersistence.Migrator
# Optional: if bridging schemas to EF Core DbContext at runtime
dotnet add package SharpPersistence.Migrator.EfCore
```

```powershell [CLI Tool]
dotnet tool install --global SharpPersistence.Migrator.Cli
# Or as a local tool manifest:
dotnet new tool-manifest
dotnet tool install SharpPersistence.Migrator.Cli
```

:::

## Database Drivers

`SharpPersistence.Migrator` loads the ADO.NET database driver automatically from your migrator project's dependencies. Install the driver package for your target database:

::: code-group

```powershell [PostgreSQL]
dotnet add package Npgsql
```

```powershell [Oracle]
dotnet add package Oracle.ManagedDataAccess.Core
```

:::

## See Also

- [Quick Start: Database Migrator](/getting-started/quick-start-migrator)
- [Quick Start: Repositories & Unit of Work](/getting-started/quick-start-repositories)
- [Quick Start: SQL Expressions](/getting-started/quick-start-expressions)
