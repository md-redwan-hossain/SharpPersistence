# Global Conventions & Entity Replacements

In larger applications and modular monoliths, repeating foreign keys, primary keys, lookup indexes, and string lengths across dozens of `IEntityConfiguration<T>` classes leads to boilerplate—or complex reflection loops in `OnModelCreating`.

`SharpPersistence.Migrator` lets you declare those rules once with **compile-time type safety** and **zero reflection** in your application code.

## How Precedence Works

Conventions and explicit configurations always run in a predictable order:

1. **CLR property scanning** — Discovers scalar properties, infers nullability (`long` vs `long?`), and sets default `Id` primary keys.
2. **`ConfigureProperties<TProperty>`** — Applies global rules by CLR type (e.g., all `string`, `decimal`, or `Enum` properties).
3. **`ConfigureEntities<TEntity>`** — Applies rules to every entity implementing an interface or inheriting a base class, in registration order.
4. **Per-entity `IEntityConfiguration<T>`** — Runs last, so any entity can override a conventional foreign key, index, column, or table name without creating duplicates.

## Global Property Conventions

Use `ConfigureProperties<T>` for defaults that apply by property type across every entity:

```csharp
opt.ConfigureProperties<string>(c => c.HasMaxLength(200))
   .ConfigureProperties<decimal>(c => c.HasPrecision(18, 4))
   .ConfigureProperties<Enum>(c => c.HasMaxLength(50));
```

## Conventional Foreign Keys (Reflection-Free)

Because C#'s `where TEntity : class` constraint accepts interfaces as well as classes, `ConfigureEntities<T>` gives you full IntelliSense for shared domain contracts. Nullability (`Restrict` for `long`, `SetNull` for `long?`) is already inferred from the entity property:

```csharp
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;

public static class CommonMigratorConventions
{
    public static void Configure(DbMigratorOptionsBuilder options)
    {
        // Audit & approval tracking
        options.ConfigureEntities<ICreateTracking>(e =>
            e.HasForeignKey(x => x.CreatedByUserId)
                .References<RefUser>(u => u.Id)
                .OnDelete(DbReferentialAction.Restrict));

        options.ConfigureEntities<IUpdateTracking>(e =>
            e.HasForeignKey(x => x.UpdatedByUserId)
                .References<RefUser>(u => u.Id)
                .OnDelete(DbReferentialAction.SetNull));

        options.ConfigureEntities<IApproveTracking>(e =>
            e.HasForeignKey(x => x.ApprovedByUserId)
                .References<RefUser>(u => u.Id)
                .OnDelete(DbReferentialAction.SetNull));

        options.ConfigureEntities<ILastActivityTracking>(e =>
            e.HasForeignKey(x => x.LastActivityByUserId)
                .References<RefUser>(u => u.Id)
                .OnDelete(DbReferentialAction.SetNull));

        options.ConfigureEntities<IMultilevelApproval>(e =>
            e.HasForeignKey(x => x.ConcernUserId)
                .References<RefUser>(u => u.Id)
                .OnDelete(DbReferentialAction.Restrict));

        // Tenant, fiscal year & bookkeeping contracts
        options.ConfigureEntities<ICompany<long>>(e =>
            e.HasForeignKey(x => x.CompanyId)
                .References<RefCompany>(c => c.Id)
                .OnDelete(DbReferentialAction.Restrict));

        options.ConfigureEntities<IOptionalFiscalYearTrackableEntity>(e =>
            e.HasForeignKey(x => x.FiscalYearId)
                .References<RefFiscalYear>(f => f.Id)
                .OnDelete(DbReferentialAction.SetNull));

        options.ConfigureEntities<IMandatoryFiscalYearTrackableEntity>(e =>
            e.HasForeignKey(x => x.FiscalYearId)
                .References<RefFiscalYear>(f => f.Id)
                .OnDelete(DbReferentialAction.Restrict));

        options.ConfigureEntities<IOptionalBookKeepingEntity<long>>(e =>
            e.HasForeignKey(x => x.AccountHeadId)
                .References<RefAccountHead>(a => a.Id)
                .OnDelete(DbReferentialAction.SetNull));

        options.ConfigureEntities<IMandatoryBookKeepingEntity<long>>(e =>
            e.HasForeignKey(x => x.AccountHeadId)
                .References<RefAccountHead>(a => a.Id)
                .OnDelete(DbReferentialAction.Restrict));
    }
}
```

## Keys, Lookups & Table Naming Conventions

`ConfigureEntities<T>` also handles primary keys, identity columns, schemas, max lengths, and indexes—alongside `MapTableNames` for custom table prefixes:

```csharp
// Prefix lookup tables with lk_ (e.g., Currency -> lk_currencies)
options.MapTableNames((entityType, defaultTableName) =>
{
    var isLookup = !entityType.Name.StartsWith("Lk", StringComparison.Ordinal)
        && entityType.GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ILookupEntity<,>));

    return isLookup ? $"lk_{defaultTableName}" : defaultTableName;
});

// Identity PK for long-keyed entities
options.ConfigureEntities<IAutoIncrementalEntity<long>>(e =>
{
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).UseIdentityAlwaysColumn();
});

// Unique lookup code with PostgreSQL varchar_pattern_ops index
options.ConfigureEntities<ILookupCode<string>>(e =>
{
    e.Property(x => x.LookupCode).HasMaxLength(32);
    e.HasIndex(x => x.LookupCode)
        .IsUnique()
        .HasOperators(x => x.LookupCode, "varchar_pattern_ops");
});

// Shared lookup table layout
options.ConfigureEntities<ILookupEntity<short, string>>(e =>
{
    e.HasSchema("lookup");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).ValueGeneratedNever();
    e.Property(x => x.Title).HasMaxLength(150);
    e.HasIndex(x => x.Title).IsUnique();
});
```

## Overriding a Convention on One Entity

Because per-entity `IEntityConfiguration<T>` runs after `ConfigureEntities<T>`, you can override a conventional foreign key or index on a specific entity without writing `if (!alreadyExists)` checks or producing duplicate constraints:

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

[EntityConfiguration<Currency>]
public sealed class CurrencyConfiguration : IEntityConfiguration<Currency>
{
    public void Configure(EntityBuilder<Currency> builder)
    {
        // Customizes the conventional unique index on Title without creating a second index
        builder.HasIndex(x => x.Title)
            .HasDatabaseName("uq_custom_currency_title");
    }
}
```

## Cross-Module Entity Type Replacements (`ReplaceEntityTypes`)

In a modular monolith, modules should not reference each other's domain entities directly. Instead, each module references lightweight `Ref*` types (such as `RefUser` or `RefCompany`) in `Common.Domain`, and your composition root (`AppRoot`) replaces each reference type with its owning entity once via `ReplaceEntityTypes`.

::: code-group

```csharp [Common.Domain (Reference Types)]
// Lightweight reference entity in Common.Domain — zero dependency on AccessControl or Billing
public sealed class RefUser
{
    public long Id { get; set; }
}
```

```csharp [Billing.Infrastructure (Module)]
[EntityConfiguration<Invoice>]
public sealed class InvoiceConfiguration : IEntityConfiguration<Invoice>
{
    public void Configure(EntityBuilder<Invoice> builder)
    {
        // Explicit cross-module FK using the local RefUser type
        builder.HasForeignKey(x => x.ApprovedByUserId)
            .References<RefUser>(u => u.Id)
            .OnDelete(DbReferentialAction.SetNull);
    }
}
```

```csharp [App.Api (Startup / Composition Root)]
builder.Services.AddDbMigrator(opt =>
{
    opt.UsePostgreSql(connectionString)
        .HasPostgresExtension("pg_trgm")
        .HasPostgresExtension("btree_gist");

    opt.UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
        .PluralizeTableNames();

    // Shared interface & naming conventions
    CommonMigratorConventions.Configure(opt);

    // Replace each Ref* entity with its real owning entity once
    opt.ReplaceEntityTypes(r =>
    {
        r.Replace<RefUser, User>()
         .Replace<RefCompany, Company>()
         .Replace<RefAccountHead, AccountHead>()
         .Replace<RefFiscalYear, FiscalYear>();
    });

    opt.AddGeneratedEntityRegistries();
});
```

:::

With `ReplaceEntityTypes` in place:

* **No extra registration needed** — `RefUser` does not need to be added to any `IEntityRegistry`.
* **Accurate topological ordering** — `AccessControlRegistry` (which owns `User`) is automatically ordered before `BillingRegistry`.
* **Clean SQL constraint names** — `billing.invoices` gets `fk_invoices_users_created_by_user_id` referencing `access_control.users(id)` (never `ref_users`).
* **Self-contained owning modules** — Entities inside `AccessControlRegistry` (such as `Role` or `User`) that also implement `ICreateTracking` resolve directly to `access_control.users(id)`.
* **Validated by `db-migrator doctor`** — Checks that every replacement target is registered, not marked `ExcludeFromMigrations()`, and not chained to another replaced entity.
