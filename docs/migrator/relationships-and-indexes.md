# Relationships & Indexes

## Foreign Keys

Define foreign keys fluently with `HasForeignKey(...).References<TPrincipal>(...)`:

```csharp
using SharpPersistence.Migrator.Enums;

// Standard many-to-one foreign key
builder.HasForeignKey(x => x.AssignedUserId)
    .References<User>(u => u.Id)
    .HasConstraintName("fk_todos_users_assigned_user_id")
    .OnDelete(DbReferentialAction.Restrict);

// Composite foreign key
builder.HasForeignKey(x => new { x.TenantId, x.CustomerId })
    .References<Customer>(c => new { c.TenantId, c.Id })
    .HasConstraintName("fk_orders_customers")
    .OnDelete(DbReferentialAction.Cascade);

// One-to-one foreign key (automatically emits a unique index on the FK column)
builder.HasForeignKey(x => x.UserId)
    .References<User>(u => u.Id)
    .IsOneToOne()
    .HasConstraintName("fk_profiles_users_user_id");
```

### Circular Foreign Keys (Deferred Scripts)

When two registries or tables reference each other in a cycle, mark one side with `.IsCircular()`:

```csharp
builder.HasForeignKey(x => x.PrimaryAddressId)
    .References<Address>(a => a.Id)
    .IsCircular();
```

`db-migrator diff` separates circular foreign keys into `MigrationScripts/_deferred/` so both tables are created before the circular `ALTER TABLE ... ADD CONSTRAINT` runs.

### Referential Actions (`DbReferentialAction`)

* `DbReferentialAction.NoAction` *(default)*
* `DbReferentialAction.Restrict`
* `DbReferentialAction.Cascade`
* `DbReferentialAction.SetNull`

## Indexes

```csharp
// Simple index with explicit name
builder.HasIndex(x => x.CustomerId)
    .HasDatabaseName("ix_orders_customer_id");

// Unique filtered index (type-safe filter expression)
builder.HasIndex(x => x.Email)
    .IsUnique()
    .HasDatabaseName("uq_users_active_email")
    .HasFilter(cc => cc.Column(x => x.IsDeleted).EqualTo(false));

// Composite covering index with sort direction
builder.HasIndex(x => new { x.TenantId, x.CreatedAt })
    .HasDatabaseName("ix_orders_tenant_created")
    .IsDescending(false, true)
    .IncludeProperties(x => new { x.Total, x.Status });
```

### Index Methods Reference

| Method | Purpose |
| --- | --- |
| `HasDatabaseName(name)` | Explicit index name (auto-truncated to dialect max identifier length) |
| `IsUnique(bool = true)` | Creates `UNIQUE INDEX` |
| `HasFilter(sql)` | Raw partial index `WHERE` predicate |
| `HasFilter(cc => ...)` | Type-safe partial index `WHERE` predicate using `SqlCheckConstraintBuilder<T>` |
| `IncludeProperties(x => ...)` | Covering index `INCLUDE (...)` columns |
| `IsDescending(params bool[])` | Per-column `ASC` / `DESC` sort order |
| `HasMethod(...)` | PostgreSQL index method (`BTree`, `Gin`, `Gist`, `Hash`, etc.) |
| `UseTablespace(name)` | PostgreSQL index tablespace |
| `HasStorageParameter(...)` | PostgreSQL index `WITH (...)` parameters |
