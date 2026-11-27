# Migrating from EF Core

Move an existing EF Core codebase to `SharpPersistence.Migrator` in four explicit CLI steps without losing your migration history.

## Step 0 (Optional): Annotate Existing `DbContext` Entities

If your domain entities aren't yet marked with `[Entity]` (or `[Entity<TGroup>]`), annotate every entity discovered from your EF Core `DbContext` in one pass:

```powershell
db-migrator annotate-dbcontext-entities --dbcontext src/Infrastructure/AppDbContext.cs --search-directory src/Domain --dry-run
```

## Step 1: Translate `IEntityTypeConfiguration<T>` Files

Run `translate-ef-configs` on your configurations folder (this runs only when you invoke the command):

```powershell
# Preview first without modifying files
db-migrator translate-ef-configs --source-path src/Infrastructure/Configurations --dry-run

# Write translated files into <source-path>/TranslatedEfConfigs (or pass --output-path for a custom folder)
db-migrator translate-ef-configs --source-path src/Infrastructure/Configurations --output-path src/Infrastructure/MigratorConfigs
```

### What It Rewrites Automatically

* `IEntityTypeConfiguration<T>` → `IEntityConfiguration<T>`, `EntityTypeBuilder<T>` → `EntityBuilder<T>`, adds `sealed` and `[EntityConfiguration<T>]`
* `builder.ToTable("orders", "sales")` → `builder.ToTable("orders").HasSchema("sales")`, and `builder.Metadata.HasSchema("sales")` / `SetSchema("sales")` → `builder.HasSchema("sales")`
* `...Metadata.SetAnnotation(...)` and `...HasAnnotation(...)` → `...SetAnnotation(...)` (on both entities and properties)
* Flattens `builder.ToTable("orders", t => { t.HasCheckConstraint(...); t.ExcludeFromMigrations(); })`
* Rewrites `HasOne<TPrincipal>().WithMany()/WithOne().HasForeignKey(...).HasConstraintName(...).OnDelete(DeleteBehavior.X)` → `HasForeignKey(...).References<TPrincipal>(...).IsOneToOne().HasConstraintName(...).OnDelete(DbReferentialAction.X)`
* Rewrites `HasMethod("gin")` → `HasMethod(PostgresIndexMethod.Gin)`
* Updates `using` directives and flags EF-only calls (`OwnsOne`, `HasData`, `HasTrigger`, etc.) with `// TODO(SharpPersistence.Migrator): ...` comments

## Step 2: Translate Existing EF Core Migrations to SQL

Translate existing EF Core C# migration files 1-to-1 into `DbMigrator/Scripts/_translated/`:

```powershell
db-migrator translate-ef-migrations --source-path src/Infrastructure/Migrations --startup-project src/Api/Api.csproj
```

## Step 3: Sync `__EFMigrationsHistory`

Mark already-applied `_translated` scripts as applied in `__db_migrator_logs` so `migrate` won't re-run historical scripts:

```powershell
# Preview what will be synced
db-migrator sync-ef-history --startup-project src/Api/Api.csproj --dry-run

# Sync history
db-migrator sync-ef-history --startup-project src/Api/Api.csproj
```

Finally, run `db-migrator doctor --check-db` to confirm your translated configs, registries, and live database match.
