using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
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
                    .HasTablespace("fast_ssd")
                    .HasStorageParameter(PostgresStorageParameter.Fillfactor, 80)
                    .HasStorageParameter(PostgresStorageParameter.AutovacuumVacuumScaleFactor, 0.05)
                    .HasStorageParameter(PostgresStorageParameter.AutovacuumEnabled, true);

                b.HasIndex(x => x.Email)
                    .HasTablespace("idx_disk")
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
                    .HasTablespace("archive_disk")
                    .HasStorageParameter(PostgresStorageParameter.Fillfactor, 90);

                b.HasIndex(x => x.Email)
                    .HasTablespace("idx_archive")
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
}
