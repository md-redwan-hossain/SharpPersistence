# PostgreSQL Features

`SharpPersistence.Migrator.PostgreSql` provides first-class PostgreSQL DDL support.

```csharp
using SharpPersistence.Migrator.PostgreSql;
```

## Extensions & Native Enums

Register PostgreSQL extensions and enums inside `UsePostgreSql(...)`:

```csharp
builder.Services.AddDbMigrator(opt =>
{
    opt.UsePostgreSql(connectionString)
        .HasPostgresExtension("btree_gist")
        .HasPostgresExtension("pg_trgm", schema: "extensions")
        .HasPostgresEnum<BookingStatus>("booking_status");

    opt.AddGeneratedEntityRegistries();
});
```

Global extensions and enums are emitted into `MigrationScripts/_global/` before registry tables are created.

## Tables: Unlogged, Tablespace & Storage Parameters

```csharp
builder.ToTable("session_cache")
    .IsUnlogged()
    .UseTablespace("fast_ssd")
    .HasStorageParameter(PostgresStorageParameter.FillFactor, 80)
    .HasStorageParameter(PostgresStorageParameter.AutovacuumEnabled, true);
```

## Indexes: Methods, Operator Classes, Tablespace & Storage Parameters

```csharp
// GIN index for full-text search or JSONB
builder.HasIndex(x => x.Metadata)
    .HasDatabaseName("ix_events_metadata_gin")
    .HasMethod(PostgresIndexMethod.Gin)
    .HasOperator("jsonb_path_ops")
    .HasStorageParameter(PostgresIndexStorageParameter.FastUpdate, true)
    .UseTablespace("fast_ssd");
```

Available `PostgresIndexMethod` values: `BTree`, `Hash`, `Gist`, `SpGist`, `Gin`, `Brin`.

## Exclusion Constraints (`EXCLUDE USING`)

Create PostgreSQL exclusion constraints fluently with `HasExclusionConstraint(name)`:

```csharp
builder.HasExclusionConstraint("excl_bookings_room_slot")
    .HasMethod(PostgresIndexMethod.Gist)
    .WithElement(x => x.RoomId, PostgresExclusionOperator.Equals)
    .WithRange(x => x.StartsAt, x => x.EndsAt, PostgresRangeBounds.InclusiveLowerExclusiveUpper)
    .IncludeProperties(x => x.CustomerId)
    .HasFilter(expr => expr.Column(x => x.IsCancelled).EqualTo(false))
    .HasStorageParameter(PostgresIndexStorageParameter.FillFactor, 90)
    .UseTablespace("fast_ssd");
```

You can also use `WithExpression` with `ISqlExpressionBuilder<TEntity>` for custom functions or raw SQL:

```csharp
builder.HasExclusionConstraint("excl_bookings_lower_code")
    .WithExpression(expr => expr.Column(x => x.Code).Lower(), PostgresExclusionOperator.Equals)
    .WithRange(x => x.StartsAt, x => x.EndsAt, PostgresExclusionOperator.Overlaps);
```

::: warning Requires `btree_gist` for Scalar Columns
When combining scalar equality (`=`) with GiST range overlaps (`&&`), add `.HasPostgresExtension("btree_gist")`. `db-migrator doctor` automatically warns if `btree_gist` is missing.
:::

## `xmin` System Concurrency Token

Properties of type `uint` marked with `.IsRowVersion()` automatically map to PostgreSQL's built-in `xmin` (`xid`) system column without creating an extra physical column.
