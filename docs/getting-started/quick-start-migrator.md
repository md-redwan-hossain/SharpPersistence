# Quick Start: Database Migrator

This article shows you how to configure `SharpPersistence.Migrator`, validate your schema offline, and generate and apply your first SQL migration script using a dedicated migrator console project.

## Prerequisites

- .NET 10 or .NET 11 SDK
- A PostgreSQL or Oracle database instance
- The `db-migrator` CLI tool

## 1. Install the Packages and CLI Tool

Install `SharpPersistence.Abstractions` in your domain project, `SharpPersistence.Migrator` and a database driver in your infrastructure and migrator console projects, and the `SharpPersistence.Migrator.Cli` tool:

```powershell
dotnet add MyApp.Domain package SharpPersistence.Abstractions
dotnet add MyApp.Infrastructure package SharpPersistence.Migrator
dotnet add MyApp.DbMigrator package SharpPersistence.Migrator
dotnet add MyApp.DbMigrator package Npgsql
dotnet tool install --global SharpPersistence.Migrator.Cli
```

::: info
Use `Oracle.ManagedDataAccess.Core` instead of `Npgsql` when targeting Oracle Database.
:::

## 2. Mark Entities in the Domain Project

Decorate entity classes in `MyApp.Domain` with `[Entity]` (or `[Entity<TGroup>]` for modular solutions):

```csharp
using SharpPersistence.Abstractions.Attributes;

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

## 3. Configure Entities and Declare a Registry

In `MyApp.Infrastructure`, create `IEntityConfiguration<T>` classes and a `sealed partial` `[EntityRegistry]` class:

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
public sealed partial class AppEntityRegistry : IEntityRegistry;
```

## 4. Configure the Dedicated Migrator Console Project

In `MyApp.DbMigrator/AssemblyInfo.cs`, mark the assembly as the migrator root so `AddGeneratedEntityRegistries()` is generated at compile time:

```csharp
using SharpPersistence.Migrator.Attributes;

[assembly: EntityRegistryRoot]
```

In `MyApp.DbMigrator/Program.cs`, register `AddDbMigrator(...)` with your database provider:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Generated;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbMigrator(opt =>
{
    opt.UsePostgreSql(builder.Configuration.GetConnectionString("Default")!)
        .AddGeneratedEntityRegistries();
});

using var host = builder.Build();
```

## 5. Validate, Generate, and Apply Migrations

Run `db-migrator` against `MyApp.DbMigrator`:

```powershell
# Validate DI wiring, entity configurations, and foreign key types offline
db-migrator doctor --project MyApp.DbMigrator/MyApp.DbMigrator.csproj

# Generate idempotent SQL scripts and update DbMigrator/Snapshots
db-migrator generate --name Initial --project MyApp.DbMigrator/MyApp.DbMigrator.csproj

# Verify live database connectivity and apply pending scripts
db-migrator doctor --check-db --project MyApp.DbMigrator/MyApp.DbMigrator.csproj
db-migrator migrate --project MyApp.DbMigrator/MyApp.DbMigrator.csproj
```

## See Also

- [Setup, DI & Runtime Execution](/migrator/setup-and-di)
- [Entities and Configurations](/migrator/entities-and-configurations)
- [`db-migrator` CLI Reference](/cli/commands)
