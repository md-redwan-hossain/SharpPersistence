using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.PostgreSql;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class PostgresDialectGeneratorTests
{
    private readonly PostgresDialectGenerator _generator = new();

    [Fact]
    public void GenerateSql_CreateTable_EmitsIdempotentTransactionAndSkipsSystemColumns()
    {
        var table = new TableSchema
        {
            EntityName = "User",
            TableName = "users",
            Schema = "public",
            PrimaryKey = new PrimaryKeySchema { Name = "pk_users", ColumnNames = ["id"], PropertyNames = ["Id"] },
            Columns =
            [
                new ColumnSchema { PropertyName = "Id", ColumnName = "id", SqlType = "uuid", IsNullable = false },
                new ColumnSchema { PropertyName = "Email", ColumnName = "email", SqlType = "character varying(256)", IsNullable = false },
                new ColumnSchema { PropertyName = "Version", ColumnName = "xmin", SqlType = "xid", IsNullable = false, IsSystemColumn = true }
            ]
        };

        var sql = _generator.GenerateSql([new CreateTableOp(table)]);

        Assert.Contains("BEGIN;", sql);
        Assert.Contains("COMMIT;", sql);
        Assert.Contains("CREATE TABLE IF NOT EXISTS \"public\".\"users\"", sql);
        Assert.Contains("\"email\" character varying(256) NOT NULL", sql);
        Assert.DoesNotContain("\"xmin\"", sql);
    }

    [Fact]
    public void GenerateSql_AddCheckConstraint_UsesPgCatalogGuard()
    {
        var op = new AddCheckConstraintOp(
            "public",
            "users",
            new CheckConstraintSchema { Name = "ck_users_role", Sql = "role IN ('admin', 'user')" });

        var sql = _generator.GenerateSql([op]);

        Assert.Contains("pg_catalog.pg_constraint", sql);
        Assert.Contains("role IN ('admin', 'user')", sql);
    }

    private sealed class SearchUser
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class OperatorRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<SearchUser>(b =>
            {
                b.ToTable("search_users");

                b.HasIndex(x => x.Email)
                    .HasMethod(PostgresIndexMethod.Gin)
                    .HasOperator("gin_trgm_ops");

                b.HasIndex(x => new { x.TenantId, x.Email })
                    .HasOperator(k => k.Email, "varchar_pattern_ops")
                    .IncludeProperties(x => x.Name);
            });
        }
    }

    [Fact]
    public void GenerateDiff_PostgresIndexOperators_EmitsSingleAndCompositeOperators()
    {
        var registry = new OperatorRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains("USING gin (\"email\" gin_trgm_ops);", diff.SqlScript);
        Assert.Contains("(\"tenant_id\", \"email\" varchar_pattern_ops) INCLUDE (\"name\");", diff.SqlScript);
    }

    private sealed class StorageRegistryV1 : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<SearchUser>(b =>
            {
                b.ToTable("search_users")
                    .IsUnlogged()
                    .UseTablespace("fast_ssd")
                    .HasStorageParameter(PostgresStorageParameter.Fillfactor, 80)
                    .HasStorageParameter(PostgresStorageParameter.AutovacuumVacuumScaleFactor, 0.05)
                    .HasStorageParameter(PostgresStorageParameter.AutovacuumEnabled, true);

                b.HasIndex(x => x.Email)
                    .UseTablespace("idx_disk")
                    .HasStorageParameter(PostgresIndexStorageParameter.Fillfactor, 70)
                    .HasStorageParameter(PostgresIndexStorageParameter.DeduplicateItems, false);
            });
        }
    }

    private sealed class StorageRegistryV2 : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<SearchUser>(b =>
            {
                b.ToTable("search_users")
                    .IsUnlogged(false)
                    .UseTablespace("archive_disk")
                    .HasStorageParameter(PostgresStorageParameter.Fillfactor, 90);

                b.HasIndex(x => x.Email)
                    .UseTablespace("idx_archive")
                    .HasStorageParameter(PostgresIndexStorageParameter.Fillfactor, 85);
            });
        }
    }

    [Fact]
    public void GenerateDiff_PostgresUnloggedTablespaceAndStorageParameters_InitialAndIncremental()
    {
        var reg1 = new StorageRegistryV1();
        var schema1 = SchemaScanner.ScanRegistry(reg1, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg1]);
        var initial = StandaloneSchemaDiffer.GenerateDiff(schema1, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains("CREATE UNLOGGED TABLE IF NOT EXISTS \"search_users\"", initial.SqlScript);
        Assert.Contains("WITH (", initial.SqlScript);
        Assert.Contains("autovacuum_enabled = true", initial.SqlScript);
        Assert.Contains("autovacuum_vacuum_scale_factor = 0.05", initial.SqlScript);
        Assert.Contains("fillfactor = 80", initial.SqlScript);
        Assert.Contains("TABLESPACE \"fast_ssd\";", initial.SqlScript);
        Assert.Contains("WITH (deduplicate_items = false, fillfactor = 70) TABLESPACE \"idx_disk\";", initial.SqlScript);

        var reg2 = new StorageRegistryV2();
        var schema2 = SchemaScanner.ScanRegistry(reg2, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg2]);
        var incremental = StandaloneSchemaDiffer.GenerateDiff(schema2, initial.UpdatedSnapshotJson, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains("ALTER TABLE IF EXISTS \"search_users\" SET LOGGED;", incremental.SqlScript);
        Assert.Contains("ALTER TABLE IF EXISTS \"search_users\" SET TABLESPACE \"archive_disk\";", incremental.SqlScript);
        Assert.Contains("ALTER TABLE IF EXISTS \"search_users\" SET (fillfactor = 90);", incremental.SqlScript);
        Assert.Contains("ALTER TABLE IF EXISTS \"search_users\" RESET (autovacuum_enabled, autovacuum_vacuum_scale_factor);", incremental.SqlScript);
        Assert.Contains("ALTER INDEX IF EXISTS \"ix_search_users_email\" SET TABLESPACE \"idx_archive\";", incremental.SqlScript);
        Assert.Contains("ALTER INDEX IF EXISTS \"ix_search_users_email\" SET (fillfactor = 85);", incremental.SqlScript);
        Assert.Contains("ALTER INDEX IF EXISTS \"ix_search_users_email\" RESET (deduplicate_items);", incremental.SqlScript);
    }

    private sealed class RoomBooking
    {
        public Guid Id { get; set; }
        public string Room { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public string GuestName { get; set; } = string.Empty;
        public DateTimeOffset? CancelledAt { get; set; }
    }

    private sealed class BookingExclusionRegistryV1 : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<RoomBooking>(b =>
            {
                b.ToTable("room_bookings");
                b.Property(x => x.Period).HasColumnType("tstzrange");

                b.HasExclusionConstraint("ex_bookings_no_overlap")
                    .HasMethod(PostgresIndexMethod.Gist)
                    .WithElement(x => x.Room, PostgresExclusionOperator.Equals)
                    .WithElement(x => x.Period, PostgresExclusionOperator.Overlaps)
                    .IncludeProperties(x => x.GuestName)
                    .HasStorageParameter(PostgresIndexStorageParameter.Fillfactor, 80)
                    .UseTablespace("idx_disk")
                    .HasFilter(cc => cc.Column(x => x.CancelledAt).IsNull());
            });
        }
    }

    private sealed class BookingExclusionRegistryV2Renamed : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<RoomBooking>(b =>
            {
                b.ToTable("room_bookings");
                b.Property(x => x.Period).HasColumnType("tstzrange");

                b.HasExclusionConstraint("ex_room_bookings_no_overlap")
                    .HasMethod(PostgresIndexMethod.Gist)
                    .WithElement(x => x.Room, PostgresExclusionOperator.Equals)
                    .WithElement(x => x.Period, PostgresExclusionOperator.Overlaps)
                    .IncludeProperties(x => x.GuestName)
                    .HasStorageParameter(PostgresIndexStorageParameter.Fillfactor, 80)
                    .UseTablespace("idx_disk")
                    .HasFilter(cc => cc.Column(x => x.CancelledAt).IsNull());
            });
        }
    }

    private sealed class BookingExclusionRegistryV3Dropped : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<RoomBooking>(b =>
            {
                b.ToTable("room_bookings");
                b.Property(x => x.Period).HasColumnType("tstzrange");
            });
        }
    }

    [Fact]
    public void GenerateDiff_PostgresExclusionConstraint_InitialRenameAndDrop()
    {
        var reg1 = new BookingExclusionRegistryV1();
        var schema1 = SchemaScanner.ScanRegistry(reg1, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg1]);
        var initial = StandaloneSchemaDiffer.GenerateDiff(schema1, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains(
            "CONSTRAINT \"ex_bookings_no_overlap\" EXCLUDE USING gist (\"room\" WITH =, \"period\" WITH &&) INCLUDE (\"guest_name\") WITH (fillfactor = 80) USING INDEX TABLESPACE \"idx_disk\" WHERE (\"cancelled_at\" IS NULL);",
            initial.SqlScript);

        var reg2 = new BookingExclusionRegistryV2Renamed();
        var schema2 = SchemaScanner.ScanRegistry(reg2, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg2]);
        var renamed = StandaloneSchemaDiffer.GenerateDiff(schema2, initial.UpdatedSnapshotJson, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains("RENAME CONSTRAINT \"ex_bookings_no_overlap\" TO \"ex_room_bookings_no_overlap\";", renamed.SqlScript);

        var reg3 = new BookingExclusionRegistryV3Dropped();
        var schema3 = SchemaScanner.ScanRegistry(reg3, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg3]);
        var dropped = StandaloneSchemaDiffer.GenerateDiff(schema3, renamed.UpdatedSnapshotJson, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains("DROP CONSTRAINT IF EXISTS \"ex_room_bookings_no_overlap\";", dropped.SqlScript);
    }

    private enum BookingStatus
    {
        Active,
        Cancelled
    }

    private sealed class FluentConventionsRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<RoomBooking>(b =>
            {
                b.ToTable("room_bookings");
                b.HasKey(x => x.Id).HasName("pk_custom_room_bookings");
                b.HasIndex(x => x.Room)
                    .HasDatabaseName("ix_active_room_bookings")
                    .HasFilter(cc => cc.Column(x => x.CancelledAt).IsNull());
            });
        }
    }

    [Fact]
    public void EfCoreConventions_KeyBuilderIndexDatabaseNameTypedFilterAndOptions()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .HasPostgresExtension("btree_gist")
            .HasPostgresEnum<BookingStatus>();

        var options = optionsBuilder.Build();
        Assert.Single(options.PostgreSql.Extensions, e => e.Name == "btree_gist");
        Assert.Single(options.PostgreSql.Enums, e => e.Name == "booking_status");

        var reg = new FluentConventionsRegistry();
        var schema = SchemaScanner.ScanRegistry(reg, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg]);
        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains("CONSTRAINT \"pk_custom_room_bookings\" PRIMARY KEY (\"id\")", diff.SqlScript);
        Assert.Contains("CREATE INDEX IF NOT EXISTS \"ix_active_room_bookings\" ON \"room_bookings\" (\"room\") WHERE \"cancelled_at\" IS NULL;", diff.SqlScript);
    }

    private sealed class Reservation
    {
        public Guid Id { get; set; }
        public string RoomCode { get; set; } = string.Empty;
        public DateTimeOffset StartsAt { get; set; }
        public DateTimeOffset EndsAt { get; set; }
        public DateOnly ValidFrom { get; set; }
        public DateOnly ValidTo { get; set; }
    }

    private sealed class ReservationRangeRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Reservation>(b =>
            {
                b.ToTable("reservations");
                b.Property(x => x.StartsAt).HasColumnName("start_time");

                b.HasExclusionConstraint("ex_reservations_no_overlap")
                    .WithExpression(e => e.Column(x => x.RoomCode).Lower(), PostgresExclusionOperator.Equals)
                    .WithRange(x => x.StartsAt, x => x.EndsAt, PostgresExclusionOperator.Overlaps)
                    .WithRange(x => x.ValidFrom, x => x.ValidTo, PostgresExclusionOperator.Overlaps, PostgresRangeBounds.Inclusive);
            });
        }
    }

    [Fact]
    public void GenerateDiff_PostgresExclusionConstraint_TypedWithRangeAndWithExpression()
    {
        var reg = new ReservationRangeRegistry();
        var schema = SchemaScanner.ScanRegistry(reg, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg]);
        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains(
            "CONSTRAINT \"ex_reservations_no_overlap\" EXCLUDE USING gist (LOWER(\"room_code\") WITH =, tstzrange(\"start_time\", \"ends_at\", '[)') WITH &&, daterange(\"valid_from\", \"valid_to\", '[]') WITH &&);",
            diff.SqlScript);
    }

    private sealed class CrtpChainedIndexRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<SearchUser>(b =>
            {
                b.ToTable("search_users");

                // Base CRTP methods (HasDatabaseName, IsUnique, IncludeProperties) must preserve IndexBuilder<TEntity, TKey>
                // so HasOperator(k => k.Email, ...) remains directly chainable.
                b.HasIndex(x => new { x.TenantId, x.Email })
                    .HasDatabaseName("uq_crtp_tenant_email")
                    .IsUnique()
                    .IncludeProperties(x => x.Name)
                    .HasOperator(k => k.Email, "varchar_pattern_ops");
            });
        }
    }

    [Fact]
    public void IndexBuilder_CrtpChaining_PreservesKeyTypeForPostgresOperators()
    {
        var reg = new CrtpChainedIndexRegistry();
        var schema = SchemaScanner.ScanRegistry(reg, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [reg]);
        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"uq_crtp_tenant_email\" ON \"search_users\" (\"tenant_id\", \"email\" varchar_pattern_ops) INCLUDE (\"name\");",
            diff.SqlScript);
    }

    [Fact]
    public void UsePostgreSql_FluentChain_ConfiguresOptionsAndNamingConvention()
    {
        var options = new DbMigratorOptionsBuilder()
            .UsePostgreSql("Host=localhost;Database=test")
            .HasPostgresExtension("btree_gist")
            .HasPostgresEnum<BookingStatus>()
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .Build();

        Assert.Equal(SqlDialect.PostgreSql, options.Dialect);
        Assert.Equal(SqlIdentifierCase.LowerSnakeCase, options.IdentifierCase);
        Assert.Single(options.PostgreSql.Extensions, e => e.Name == "btree_gist");
        Assert.Single(options.PostgreSql.Enums, e => e.Name == "booking_status");
    }
}
