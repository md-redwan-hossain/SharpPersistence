# Compile-Time Entity Discovery

`SharpPersistence.Migrator.Generators` is a C# source generator included with `SharpPersistence.Abstractions` (it is automatically available in every project that references `Abstractions` or `Migrator`). Entity and registry discovery happens entirely at compile time—no slow startup reflection needed.

## How It Works Across Projects

1. **Domain project** (`SharpPersistence.Abstractions` only):
   Entities marked with `[Entity]` (or a custom `EntityMarkerAttribute`) generate a compile-time list of entity types for that assembly.
2. **Infrastructure / Module project** (`SharpPersistence.Migrator`):
   * `[EntityConfiguration<TEntity>]` classes are validated and paired automatically with their entities.
   * `[EntityRegistry]` (or `[EntityRegistry<TMarker>]`) classes must be `sealed partial` and implement `IEntityRegistry` + `public void Configure(IEntityRegistryBuilder registry)`.
   * The generator fills in `IEntityRegistry.Configure` to call `registry.Map<...>()` for every discovered entity, then runs your `Configure(registry)` method.
3. **Startup project (`Program.cs`)** (`[assembly: EntityRegistryRoot]`):
   Collects all discovered registries across referenced projects and generates `options.AddGeneratedEntityRegistries()`.

## Single-Project vs. Modular Monolith

::: code-group

```csharp [Single Project]
using SharpPersistence.Abstractions;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Attributes;

[assembly: EntityRegistryRoot]

[Entity]
public sealed class TodoItem
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
}

[EntityConfiguration<TodoItem>]
public sealed class TodoItemConfiguration : IEntityConfiguration<TodoItem>
{
    public void Configure(EntityBuilder<TodoItem> builder) => builder.ToTable("todos");
}

[EntityRegistry]
public sealed partial class AppRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry)
    {
        registry.HasDefaultSchema("public");
    }
}
```

```csharp [Modular Monolith]
// In Billing.Domain (references SharpPersistence.Abstractions only)
using SharpPersistence.Abstractions;

[assembly: DiscoverEntitiesBy<BillingEntityAttribute>]

public sealed class BillingEntityAttribute : EntityMarkerAttribute;

[BillingEntity]
public sealed class Invoice
{
    public Guid Id { get; set; }
}

// In Billing.Infrastructure (references Billing.Domain + SharpPersistence.Migrator)
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Attributes;

[EntityRegistry<BillingEntityAttribute>]
public sealed partial class BillingRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry)
    {
        registry.HasDefaultSchema("billing");
    }
}

// In App.Api (startup project)
[assembly: EntityRegistryRoot]
// In Program.cs:
// builder.Services.AddDbMigrator(opt => opt.UsePostgreSql(...).AddGeneratedEntityRegistries());
```

:::

## Assembly Attributes & MSBuild Properties

You can configure root mode or opt out via **either** assembly attributes (recommended, type-safe) or MSBuild `.csproj` properties:

| Option | Assembly Attribute | MSBuild Property |
| --- | --- | --- |
| Mark startup root | `[assembly: EntityRegistryRoot]` | `<EntityRegistryRoot>true</EntityRegistryRoot>` |
| Disable discovery/generator | `[assembly: DisableEntityDiscovery]` | `<DisableEntityDiscovery>true</DisableEntityDiscovery>` |

## Compiler Diagnostics (`SPM001`–`SPM007`)

| Code | Severity | Meaning |
| --- | --- | --- |
| `SPM001` | Error | `[EntityRegistry]` class (and any enclosing types) must be `sealed`, `partial`, and implement `IEntityRegistry` |
| `SPM002` | Error | `[EntityConfiguration<TEntity>]` class must be `sealed`, have an accessible parameterless constructor, and implement `IEntityConfiguration<TEntity>` |
| `SPM003` | Error | Multiple `[EntityConfiguration<TEntity>]` classes target the same `TEntity` |
| `SPM004` | Error | `[EntityRegistry]` class must declare `public void Configure(IEntityRegistryBuilder registry)` |
| `SPM005` | Error | Same entity is matched by multiple `[EntityRegistry]` classes in the same compilation |
| `SPM006` | Error | Discovered entity or custom marker attribute must be `public` |
| `SPM007` | Warning | `[EntityConfiguration<TEntity>]` is not matched by any `[EntityRegistry]` in this compilation |
