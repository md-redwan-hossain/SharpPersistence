# 5-Minute Quick Start

This walkthrough sets up compile-time entity discovery, configures PostgreSQL migrations, validates wiring with `db-migrator doctor`, generates an idempotent SQL script, and applies it.

## 1. Mark Your Domain Entities

In your **Domain** project (only `SharpPersistence.Abstractions` required):

```csharp
using SharpPersistence.Abstractions;

namespace MyApp.Domain;

[Entity]
public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
}

[Entity]
public sealed class TodoItem
{
    public long Id { get; set; }
    public Guid AssignedUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Priority { get; set; }
}
```

## 2. Configure Entities & Create a Registry

In your **Infrastructure** project (`SharpPersistence.Migrator`):

```csharp
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Attributes;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;

namespace MyApp.Infrastructure.Persistence;

[EntityConfiguration<TodoItem>]
public sealed class TodoItemConfiguration : IEntityConfiguration<TodoItem>
{
    public void Configure(EntityBuilder<TodoItem> builder)
    {
        builder.ToTable("todos");
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.HasIndex(x => x.AssignedUserId).HasDatabaseName("ix_todos_assigned_user_id");

        builder.HasForeignKey(x => x.AssignedUserId)
            .References<User>(u => u.Id)
            .HasConstraintName("fk_todos_users_assigned_user_id")
            .OnDelete(DbReferentialAction.Restrict);

        builder.HasCheckConstraint("ck_todos_priority",
            cc => cc.Column(x => x.Priority).Between(1, 5));
    }
}

[EntityRegistry]
public sealed partial class AppEntityRegistry : IEntityRegistry
{
    public void Configure(IEntityRegistryBuilder registry)
    {
        registry.HasDefaultSchema("public");
    }
}
```

## 3. Wire Up DI in `Program.cs`

Mark your startup assembly with `[assembly: EntityRegistryRoot]` so the Roslyn generator emits `AddGeneratedEntityRegistries()`:

```csharp
using SharpPersistence.Migrator.Attributes;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Generated;

[assembly: EntityRegistryRoot]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbMigrator(opt =>
{
    opt.UsePostgreSql(builder.Configuration.GetConnectionString("Default")!)
        .AddGeneratedEntityRegistries();
});
```

## 4. Validate, Generate & Apply

*(If you installed `SharpPersistence.Migrator.Cli` as a local tool instead of globally, prefix each command with `dotnet `.)*

```powershell
# 1. Validate DI configuration, entity mappings, and foreign key types offline
db-migrator doctor --startup-project MyApp.Api/MyApp.Api.csproj

# 2. Generate a safe-to-re-run SQL migration script
db-migrator diff --name Initial --startup-project MyApp.Api/MyApp.Api.csproj

# 3. Check the live database & apply pending migration scripts
db-migrator doctor --check-db --startup-project MyApp.Api/MyApp.Api.csproj
db-migrator migrate --startup-project MyApp.Api/MyApp.Api.csproj
```
