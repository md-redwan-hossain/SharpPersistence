# Implementation Details

This article explains how `SharpPersistence.Migrator` discovers entities at compile time, scans schemas in a single pass, and applies SQL scripts safely at runtime. The architecture is designed for large modular monoliths (20,000–30,000+ classes) with zero startup assembly scanning.

## Generator Packaging Split

Compile-time code generation is split into two independent Roslyn generator assemblies so domain projects stay free of infrastructure dependencies and MSBuild never loads duplicate analyzers:

| Generator Assembly | Packed In | Runs In | Responsibility |
| --- | --- | --- | --- |
| `SharpPersistence.Abstractions.Generators.dll` | `SharpPersistence.Abstractions` | Domain & shared projects | Runs `EntityIndexGenerator` (`SPM006`) to index `[Entity]` / `[Entity<TGroup>]` types |
| `SharpPersistence.Migrator.Generators.dll` | `SharpPersistence.Migrator` | Infrastructure & migrator root projects | Runs `EntityRegistryGenerator`, `DbContextBindingGenerator`, and `MigratorUsageAnalyzer` (`SPM001`–`SPM016`) |
| `SharpPersistence.Migrator.CodeFixes.dll` | `SharpPersistence.Migrator` | Infrastructure & migrator root projects | Provides IDE quick-fixes for `SPM` diagnostics |

## O(1) Cross-Assembly Compile-Time Indexing

In large solutions, walking every namespace and type across all referenced assemblies during compilation is prohibitively slow. Instead, each layer emits a single static index class in the `SharpPersistence.Migrator.Generated` namespace using a deterministic name derived from the assembly name (`Sanitize(assemblyName)` replaces non-alphanumeric characters with `_` and prepends `_` if the name starts with a digit).

### 1. Domain Layer (`EntityIndex_<AssemblyName>`)

When a Domain project compiles, `EntityIndexGenerator` emits `SharpPersistence.Migrator.EntityIndex.g.cs`:

```csharp
namespace SharpPersistence.Migrator.Generated;

[global::SharpPersistence.Abstractions.Attributes.EntityIndexAttribute(
    typeof(global::MyApp.Billing.Domain.BillingGroup),
    typeof(global::MyApp.Billing.Domain.Entities.Invoice),
    typeof(global::MyApp.Billing.Domain.Entities.InvoiceLine))]
public static class EntityIndex_MyApp_Billing_Domain
{
}
```

### 2. Infrastructure Layer (`EntityRegistryIndex_<AssemblyName>`)

When an Infrastructure project compiles, `EntityRegistryGenerator` performs one O(1) metadata lookup per non-framework referenced assembly (`refAsm.GetTypeByMetadataName("SharpPersistence.Migrator.Generated.EntityIndex_" + sanitized)`) and generates:

1. A `partial` implementation for each `[EntityRegistry]` class wiring `registry.Map<TEntity, TConfig>()` calls.
2. `SharpPersistence.Migrator.EntityRegistryIndex.g.cs` listing all registries in that assembly:

```csharp
namespace SharpPersistence.Migrator.Generated;

[global::SharpPersistence.Migrator.Attributes.EntityRegistryIndexAttribute(
    typeof(global::MyApp.Billing.Infrastructure.Persistence.BillingRegistry))]
public static class EntityRegistryIndex_MyApp_Billing_Infrastructure
{
}
```

### 3. Migrator Root Project (`AddGeneratedEntityRegistries`)

In the dedicated migrator console project marked with `[assembly: EntityRegistryRoot]` (or `<EntityRegistryRoot>true</EntityRegistryRoot>`), `EntityRegistryGenerator` looks up `EntityRegistryIndex_<AssemblyName>` in each referenced assembly and emits `GeneratedEntityRegistryExtensions.AddGeneratedEntityRegistries()`.

::: info Framework Assembly Filtering & Scoped Analyzers
Both `EntityRegistryGenerator` and `SqlTagUsageAnalyzer` skip framework assemblies (`System.*`, `Microsoft.*`, `netstandard`, `mscorlib`) when inspecting references. `MigratorUsageAnalyzer` uses `RegisterSymbolStartAction` on `SymbolKind.NamedType` so Roslyn only builds `IOperation` blocks for `[EntityRegistry]` types rather than every method in the solution.
:::

## Optional `Configure(...)` on Generated Registries

`EntityRegistryGenerator` implements `IEntityRegistry.Configure(IEntityRegistryBuilder)` using **explicit interface implementation** on the generated partial class:

```csharp
partial class BillingRegistry
{
    void global::SharpPersistence.Migrator.Abstractions.IEntityRegistry.Configure(
        global::SharpPersistence.Migrator.Abstractions.IEntityRegistryBuilder registry)
    {
        registry.Map<global::MyApp.Billing.Domain.Entities.Invoice>();
        // Emitted only if BillingRegistry declares its own Configure(IEntityRegistryBuilder) method:
        // Configure(registry);
    }
}
```

Because the explicit interface implementation satisfies `IEntityRegistry`, registries that need no custom schema, sequences, or conventions can be declared in one line:

```csharp
[EntityRegistry<BillingGroup>]
public sealed partial class BillingRegistry : IEntityRegistry;
```

Manual registries without `[EntityRegistry]` receive no generated partial and continue to enforce `IEntityRegistry.Configure(...)` at compile time.

## Schema Scanning Precedence & Single-Pass Resolution

`RegistryDependencyResolver.SortAndScanTopologically(...)` evaluates all registries and builds table definitions **once** (O(E) rather than O(R × E)), returning the topologically sorted `(IEntityRegistry, DatabaseModel)` list directly to `db-migrator generate` and `db-migrator ui`.

For each entity, configuration executes in deterministic order (later steps override earlier defaults):

1. CLR property scan (scalar columns, nullability, PK convention `Id` / `<Entity>Id`).
2. Global `ConfigureProperties<TProperty>(...)` → Registry `ConfigureProperties<TProperty>(...)`.
3. Global `ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, ...)` → Registry `ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, ...)`.
4. Per-entity `IEntityConfiguration<TEntity>.Configure(...)`.
5. Global `ConfigureEntities(ModelCreationPhase.AfterEntityConfigClass, ...)` → Registry `ConfigureEntities(ModelCreationPhase.AfterEntityConfigClass, ...)`.

## Runtime Execution, Locking & Safety

When `IDbMigrator.MigrateAsync(...)` (or `db-migrator migrate`) runs:

1. **Distributed Lock:** Acquires a database-level lock (`pg_advisory_lock` on PostgreSQL, `DBMS_LOCK` on Oracle) before inspecting history or executing DDL. `SyncEfCoreHistoryAsync` acquires the same lock when not in `--dry-run` mode.
2. **Checksum Verification:** Normalizes line endings (`\r\n` / `\r` → `\n`) and strips UTF-8 BOMs, then compares SHA-256 hashes of all previously applied scripts against `__db_migrator_logs.checksum_sha256`. Any mismatch aborts before pending DDL runs.
3. **Permission Preflight:** Parses pending SQL scripts and verifies required database privileges read-only before executing statement #1 (unless `skipPermissionCheck: true`).
4. **Execution & Command Timeout:** Applies each statement with `CommandTimeoutSeconds` (default `300` seconds; configurable via `UseCommandTimeout(seconds)`). PostgreSQL scripts execute inside a per-script transaction together with the `__db_migrator_logs` insert; Oracle executes statements sequentially because Oracle DDL auto-commits.

## See Also

- [Setup, DI & Runtime Execution](/migrator/setup-and-di)
- [Entities and Configurations](/migrator/entities-and-configurations)
- [Global Conventions & Entity Replacements](/migrator/conventions-and-entity-replacements)
- [`db-migrator` CLI Reference](/cli/commands)
