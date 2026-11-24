# Migrating from EF Core

Move an existing EF Core codebase to `SharpPersistence.Migrator` in three explicit CLI steps without losing your migration history.

## Step 1: Translate `IEntityTypeConfiguration<T>` Files

Run `translate-ef-configs` on your configurations folder (this runs only when you invoke the command):

```powershell
# Preview first without modifying files
db-migrator translate-ef-configs src/Infrastructure/Configurations --dry-run

# Write translated files into a new folder (or omit -o to overwrite in place)
db-migrator translate-ef-configs src/Infrastructure/Configurations -o src/Infrastructure/MigratorConfigs
```

### What It Rewrites Automatically

* `IEntityTypeConfiguration<T>` → `IEntityConfiguration<T>`, `EntityTypeBuilder<T>` → `EntityBuilder<T>`, adds `sealed` and `[EntityConfiguration<T>]`
* Flattens `builder.ToTable("orders", t => { t.HasCheckConstraint(...); })`
* Rewrites `HasOne<TPrincipal>().WithMany()/WithOne().HasForeignKey(...).HasConstraintName(...).OnDelete(DeleteBehavior.X)` → `HasForeignKey(...).References<TPrincipal>(...).IsOneToOne().HasConstraintName(...).OnDelete(DbReferentialAction.X)`
* Rewrites `HasMethod("gin")` → `HasMethod(PostgresIndexMethod.Gin)`
* Updates `using` directives and flags EF-only calls (`OwnsOne`, `HasData`, `HasTrigger`, etc.) with `// TODO(SharpPersistence.Migrator): ...` comments

## Step 2: Translate Existing EF Core Migrations to SQL

Translate existing EF Core C# migration files 1-to-1 into `MigrationScripts/_translated/`:

```powershell
db-migrator translate-ef-migration src/Infrastructure/Migrations --startup-project src/Api/Api.csproj
```

## Step 3: Sync `__EFMigrationsHistory`

Mark already-applied `_translated` scripts as applied in `__db_migration_logs` so `migrate` won't re-run historical scripts:

```powershell
# Preview what will be synced
db-migrator sync-ef-history --startup-project src/Api/Api.csproj --dry-run

# Sync history
db-migrator sync-ef-history --startup-project src/Api/Api.csproj
```

Finally, run `db-migrator doctor --check-db` to confirm your translated configs, registries, and live database match.
