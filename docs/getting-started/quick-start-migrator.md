# Quick Start: Database Migrator

Set up C#-first SQL migrations with compile-time entity discovery, validate your setup with `db-migrator doctor`, and apply your first migration in 5 minutes.

## Why Use This?

Managing database schemas with EF Core's runtime model builder adds startup overhead and ties your migrations to EF Core's internals. `SharpPersistence.Migrator` lets you configure tables in plain C# classes, generates readable, safe-to-re-run `.sql` scripts you can review in pull requests, and checks everything before runtime with `db-migrator doctor`.

## 1. Install Packages

```powershell
# Domain project (entities only)
dotnet add MyApp.Domain package SharpPersistence.Abstractions

# Infrastructure project (configurations & registries)
dotnet add MyApp.Infrastructure package SharpPersistence.Migrator
dotnet add MyApp.Infrastructure package Npgsql # or Oracle.ManagedDataAccess.Core

# CLI tool
dotnet tool install -g SharpPersistence.Migrator.Cli
```

## 2. Mark Entities in Domain

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

## 3. Configure Entities & Create a Registry

In `MyApp.Infrastructure`:

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

## 4. Mark `AssemblyInfo.cs` & Wire DI

In `MyApp.Api/AssemblyInfo.cs` (using `AssemblyInfo.cs` keeps assembly-level attributes in one predictable place across your solution):

```csharp
using SharpPersistence.Migrator.Attributes;

[assembly: EntityRegistryRoot]
```

In `MyApp.Api/Program.cs`:

```csharp
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Generated;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbMigrator(opt =>
{
    opt.UsePostgreSql(builder.Configuration.GetConnectionString("Default")!)
        .AddGeneratedEntityRegistries();
});
```

## 5. Validate, Generate & Apply

*(If you installed `SharpPersistence.Migrator.Cli` as a local tool, prefix each command with `dotnet `.)*

```powershell
# 1. Check DI configuration, entity configs, and foreign key types offline
db-migrator doctor --startup-project MyApp.Api/MyApp.Api.csproj

# 2. Generate safe-to-re-run SQL migration scripts
db-migrator generate --name Initial --startup-project MyApp.Api/MyApp.Api.csproj

# 3. Check the live database and apply pending scripts
db-migrator doctor --check-db --startup-project MyApp.Api/MyApp.Api.csproj
db-migrator migrate --startup-project MyApp.Api/MyApp.Api.csproj
```

Next: [Entities and Configurations](/migrator/entities-and-configurations)
