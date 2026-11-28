# Global Conventions & Entity Replacements

In larger applications and modular monoliths, repeating foreign keys, primary keys, lookup indexes, and string lengths across dozens of `IEntityConfiguration<T>` classes leads to boilerplate or complex reflection loops.

`SharpPersistence.Migrator` lets you declare those rules once—globally or per registry—with **compile-time type safety** and **zero reflection** in your application code.

## How Precedence Works

Conventions and explicit configurations always run in a predictable order:

1. **CLR property scanning** — Discovers scalar properties, infers nullability (`long` vs `long?`), and sets default `Id` primary keys.
2. **`ConfigureProperties<TProperty>`** — Applies global rules by CLR type, then registry-level `registry.ConfigureProperties<TProperty>` (overriding global for that registry).
3. **`ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, ...)`** — Applies global, then registry-level, default entity rules before per-entity configs.
4. **Per-entity `IEntityConfiguration<T>`** — Overrides any `BeforeEntityConfigClass` rule without creating duplicates, and can attach custom annotations via `SetAnnotation(...)`.
5. **`ConfigureEntities(ModelCreationPhase.AfterEntityConfigClass, ...)`** — Runs global, then registry-level, post-config rules that can inspect annotations (`GetAnnotation<T>`, `TryGetAnnotation<T>`, `HasAnnotation`) set by `IEntityConfiguration<T>`.

## Property Conventions (`ConfigureProperties<T>`)

Apply defaults by CLR type across all entities (or override per registry via `registry.ConfigureProperties<T>`):

```csharp
opt.ConfigureProperties<string>(c => c.HasMaxLength(200))
   .ConfigureProperties<decimal>(c => c.HasPrecision(18, 4))
   .ConfigureProperties<Enum>(c => c.HasMaxLength(50));
```

## Entity Rules (`ConfigureEntities`)

Use `ConfigureEntities(ModelCreationPhase, Action<EntityBuilder>)` on either `DbMigratorOptionsBuilder` (global) or `IEntityRegistryBuilder` (scoped to one registry, enforced at compile time by `SPM016`). Narrow inside the callback via `m.Entity<T>(...)` or inspect properties directly via `m.FindProperty(...)`:

```csharp
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.PostgreSql;

// 1. BeforeEntityConfigClass: defaults that per-entity IEntityConfiguration<T> can override
options.ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, m =>
{
    m.Entity<ICreateTracking>(e =>
        e.HasForeignKey(x => x.CreatedByUserId)
            .References<RefUser>(u => u.Id)
            .OnDelete(DbReferentialAction.Restrict));

    m.Entity<ILookupEntity<short, string>>(e =>
    {
        e.HasSchema("lookup");
        e.Property(x => x.Id).ValueGeneratedNever();
        e.HasIndex(x => x.Title).IsUnique();
    });
});

// 2. AfterEntityConfigClass: read per-entity annotations before adding indexes
options.ConfigureEntities(ModelCreationPhase.AfterEntityConfigClass, m =>
{
    m.Entity<IOptionalCode>(e =>
    {
        if (!e.Property(x => x.Code).GetAnnotation<bool>("SkipAutomatedIndexCreation"))
        {
            e.HasIndex(x => x.Code)
                .IsUnique()
                .HasOperator("varchar_pattern_ops");
        }
    });
});
```

## Table Naming (`MapTableNames`)

```csharp
options.MapTableNames((entityType, defaultTableName) =>
    typeof(ILookupEntity).IsAssignableFrom(entityType)
        ? $"lk_{defaultTableName}"
        : defaultTableName);
```

## Overriding a Convention on One Entity

Because per-entity `IEntityConfiguration<T>` runs after `BeforeEntityConfigClass`, you can override a conventional foreign key or index on a specific entity without writing `if (!alreadyExists)` checks or producing duplicate constraints:

```csharp
[EntityConfiguration<AuditLog>]
public sealed class AuditLogConfiguration : IEntityConfiguration<AuditLog>
{
    public void Configure(EntityBuilder<AuditLog> builder)
    {
        // Replaces the global ICreateTracking Restrict rule for AuditLog only
        builder.HasForeignKey(x => x.CreatedByUserId)
            .References<RefUser>(u => u.Id)
            .OnDelete(DbReferentialAction.NoAction);
    }
}
```

## Cross-Module Entity Type Replacements (`ReplaceEntityTypes`)

In a modular monolith, modules reference lightweight `Ref*` types (`[Entity(ReadOnly = true)]`) in `Common.Domain`, and your composition root replaces each reference type with its owning entity once via `ReplaceEntityTypes`:

```csharp
// Billing.Infrastructure: FK targets local RefUser
builder.HasForeignKey(x => x.ApprovedByUserId)
    .References<RefUser>(u => u.Id)
    .OnDelete(DbReferentialAction.SetNull);

// App.Api (Startup): replace Ref* types with owning entities once
builder.Services.AddDbMigrator(opt =>
{
    opt.UsePostgreSql(connectionString)
        .HasPostgresExtension("pg_trgm");

    opt.ReplaceEntityTypes(r =>
    {
        r.Replace<RefUser, User>()
         .Replace<RefCompany, Company>();
    });

    opt.AddGeneratedEntityRegistries();
});
```

With `ReplaceEntityTypes` in place:

* **Accurate topological ordering** — `AccessControlRegistry` (owning `User`) is automatically ordered before `BillingRegistry`.
* **Clean SQL constraint names** — `billing.invoices` gets `fk_invoices_users_approved_by_user_id` referencing `access_control.users(id)` (never `ref_users`).
* **Compile-time & doctor validation** — `SPM015` catches self-replacements and `ReadOnly` targets at build time; `db-migrator doctor` checks that every target is registered and not chained.
