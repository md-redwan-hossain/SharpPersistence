# Installation & Packages

SharpPersistence targets **.NET 10 (`net10.0`)** and **.NET 11 (`net11.0`)** and is distributed as modular NuGet packages so your Domain, Application, and Infrastructure layers stay cleanly decoupled.

## Package Matrix

| Package | Layer | Purpose |
| --- | --- | --- |
| `SharpPersistence.Abstractions` | Domain / Application | Zero-dependency repository/UoW contracts (`IRepositoryCore<T>`, `IUnitOfWork`) and compile-time discovery attributes (`[Entity]`, `EntityMarkerAttribute`) |
| `SharpPersistence` | Shared / Infrastructure | Dialect-aware `SqlCheckConstraintBuilder`, `SqlParser`, and collection/queryable helpers |
| `SharpPersistence.EfCore` | Infrastructure | EF Core implementations of `RepositoryCore<T, TDbContext>` and `UnitOfWork<TDbContext>` |
| `SharpPersistence.Migrator` | Infrastructure | EF Core–free schema scanner, differ, DDL generators (PostgreSQL & Oracle), lock service, and migration runner |
| `SharpPersistence.Migrator.EfCore` | Infrastructure | `ApplySchemaToEfCore` runtime bridge and `HasConversion` extensions for EF Core |
| `SharpPersistence.Migrator.Cli` | Global / Local Tool | `db-migrator` CLI (`doctor`, `diff`, `migrate`, `status`, `verify`, `translate-ef-configs`, `translate-ef-migration`) |

## Installing Packages

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
# Optional: if using EF Core at runtime
dotnet add package SharpPersistence.Migrator.EfCore
```

```powershell [CLI Tool]
dotnet tool install -g SharpPersistence.Migrator.Cli
# Or as a local tool manifest:
dotnet new tool-manifest
dotnet tool install SharpPersistence.Migrator.Cli
```

:::

## Database Drivers (Migrator)

`SharpPersistence.Migrator` picks up your database driver (`Npgsql` or `Oracle.ManagedDataAccess.Core`) automatically from your app's dependencies. Install the package for your database:

::: code-group

```powershell [PostgreSQL]
dotnet add package Npgsql
```

```powershell [Oracle]
dotnet add package Oracle.ManagedDataAccess.Core
```

:::
