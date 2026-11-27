using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class StandaloneSchemaDifferTests
{
    private sealed class Account
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
    }

    private sealed class Profile
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
    }

    private sealed class SampleRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Account>(b =>
            {
                b.ToTable("accounts");
                b.Property(x => x.Code).HasMaxLength(32).IsUnicode(false);
                b.Property(x => x.Bio).IsUnicode(true);
            });

            registry.Entity<Profile>(b =>
            {
                b.ToTable("profiles");
                b.HasForeignKey(x => x.AccountId)
                    .References<Account>(a => a.Id)
                    .IsOneToOne()
                    .OnDelete(DbReferentialAction.Cascade);
            });
        }
    }

    [Fact]
    public void ScanAndDiff_IsUniqueForeignKey_GeneratesSingleUniqueIndex()
    {
        var registry = new SampleRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var profile = schema.Tables.Single(t => t.TableName == "profiles");
        Assert.Single(profile.Indexes, ix => ix.IsUnique && ix.ColumnNames.SequenceEqual(["account_id"]));

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql,
            interactiveRenamePrompt: false);
        Assert.True(diff.HasChanges);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS", diff.SqlScript);

        var secondDiff = StandaloneSchemaDiffer.GenerateDiff(schema, diff.UpdatedSnapshotJson, SqlDialect.PostgreSql,
            interactiveRenamePrompt: false);
        Assert.False(secondDiff.HasChanges);
    }

    [Fact]
    public void ScanRegistry_Oracle_RespectsIsUnicode()
    {
        var registry = new SampleRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.Oracle,
            SqlIdentifierCase.UpperSnakeCase,
            [registry]);

        var account = schema.Tables.Single(t => t.TableName == "accounts");
        var code = account.Columns.Single(c => c.PropertyName == nameof(Account.Code));
        var bio = account.Columns.Single(c => c.PropertyName == nameof(Account.Bio));

        Assert.Equal("VARCHAR2(32)", code.SqlType);
        Assert.Equal("NCLOB", bio.SqlType);
    }

    private sealed class SchemaOnlyItem
    {
        public int Id { get; set; }
    }

    private sealed class ChainedSchemaItem
    {
        public int Id { get; set; }
    }

    private sealed class HasSchemaRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<SchemaOnlyItem>(e => e.HasSchema("billing"));
            registry.Entity<ChainedSchemaItem>(e => e.ToTable("custom_items").HasSchema("sales"));
        }
    }

    [Fact]
    public void HasSchema_WorksStandaloneAndChainedWithToTable()
    {
        var registry = new HasSchemaRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var schemaOnly = schema.Tables.Single(t => t.TableName == "schema_only_item");
        Assert.Equal("billing", schemaOnly.Schema);

        var chained = schema.Tables.Single(t => t.TableName == "custom_items");
        Assert.Equal("sales", chained.Schema);
    }

    private sealed class ExcludedRefUser
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ExcludedRefRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<ExcludedRefUser>(e => e.ToTable("users").ExcludeFromMigrations());
        }
    }

    [Fact]
    public void ExcludeFromMigrations_IsPresentInSchema_ButSkippedByDifferAndSnapshot()
    {
        var registry = new ExcludedRefRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var table = Assert.Single(schema.Tables);
        Assert.True(table.ExcludeFromMigrations);

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql,
            interactiveRenamePrompt: false);
        Assert.False(diff.HasChanges);
        Assert.Equal(string.Empty, diff.SqlScript);
    }

    private sealed class AnnotatedEntity
    {
        public Guid Id { get; set; }
        public string Secret { get; set; } = string.Empty;
    }

    private sealed class AnnotatedRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<AnnotatedEntity>(e =>
            {
                e.ToTable("annotated_entities")
                    .SetAnnotation("Audit:TrackChanges", true)
                    .SetAnnotation<string>("RemovedKey", "temp")
                    .SetAnnotation<string>("RemovedKey", null);

                e.Property(x => x.Secret)
                    .SetAnnotation("Sensitive:Mask", true);
            });
        }
    }

    [Fact]
    public void SetAnnotation_StoresEntityAndPropertyAnnotations_AndRemovesOnNull()
    {
        var registry = new AnnotatedRegistry();
        var schema = SchemaScanner.ScanRegistry(
            registry,
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            [registry]);

        var table = Assert.Single(schema.Tables);
        Assert.Equal(true, table.Annotations["Audit:TrackChanges"]);
        Assert.False(table.Annotations.ContainsKey("RemovedKey"));

        var col = table.Columns.Single(c => c.PropertyName == nameof(AnnotatedEntity.Secret));
        Assert.Equal(true, col.Annotations["Sensitive:Mask"]);
    }

    private sealed class IncAddress
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
    }

    private sealed class IncUser
    {
        public Guid Id { get; set; }
    }

    private sealed class IncV1Registry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Account>(b => b.ToTable("accounts"));
        }
    }

    private sealed class IncV2Registry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Account>(b => b.ToTable("accounts"));
            // "addresses" sorts before "users", yet references "users"
            registry.Entity<IncAddress>(b =>
            {
                b.ToTable("addresses");
                b.HasForeignKey(x => x.UserId).References<IncUser>(u => u.Id);
            });
            registry.Entity<IncUser>(b => b.ToTable("users"));
        }
    }

    [Fact]
    public void IncrementalDiff_CreatesAllNewTablesBeforeAddingForeignKeys()
    {
        var v1 = new IncV1Registry();
        var v1Schema = SchemaScanner.ScanRegistry(v1, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [v1]);
        var v1Diff =
            StandaloneSchemaDiffer.GenerateDiff(v1Schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        var v2 = new IncV2Registry();
        var v2Schema = SchemaScanner.ScanRegistry(v2, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [v2]);
        var v2Diff = StandaloneSchemaDiffer.GenerateDiff(v2Schema, v1Diff.UpdatedSnapshotJson, SqlDialect.PostgreSql,
            interactiveRenamePrompt: false);

        var createUsersIdx = v2Diff.SqlScript.IndexOf("CREATE TABLE IF NOT EXISTS \"users\"", StringComparison.Ordinal);
        var addFkIdx = v2Diff.SqlScript.IndexOf("fk_addresses_users_user_id", StringComparison.Ordinal);

        Assert.True(createUsersIdx >= 0);
        Assert.True(addFkIdx > createUsersIdx);
    }

    private sealed class DualIndexEntity
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public bool IsDeleted { get; set; }
    }

    private sealed class DualIndexRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<DualIndexEntity>(b =>
            {
                b.ToTable("dual_index_entities");
                b.HasIndex(x => x.Email);
                b.HasIndex(x => x.Email)
                    .IsUnique()
                    .HasDatabaseName("uq_dual_active_email")
                    .HasFilter(cc => cc.Column(x => x.IsDeleted).EqualTo(false));
            });
        }
    }

    [Fact]
    public void AddIndex_PreservesMultipleEntityIndexesOnSameProperty()
    {
        var registry = new DualIndexRegistry();
        var schema = SchemaScanner.ScanRegistry(registry, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase,
            [registry]);
        var table = Assert.Single(schema.Tables);

        Assert.Equal(2, table.Indexes.Count);
        Assert.Contains(table.Indexes, ix => !ix.IsUnique && ix.Name == "ix_dual_index_entities_email");
        Assert.Contains(table.Indexes, ix => ix.IsUnique && ix.Name == "uq_dual_active_email");
    }

    [Fact]
    public void SnapshotJson_IsCamelCaseSqlFirstAndRejectsDialectMismatch()
    {
        var registry = new DualIndexRegistry();
        var schema = SchemaScanner.ScanRegistry(registry, SqlDialect.PostgreSql, SqlIdentifierCase.LowerSnakeCase, [registry]);
        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        Assert.Contains("\"snapshotVersion\": 1", diff.UpdatedSnapshotJson);
        Assert.Contains("\"dialect\": \"PostgreSql\"", diff.UpdatedSnapshotJson);
        Assert.Contains("\"tableName\": \"dual_index_entities\"", diff.UpdatedSnapshotJson);
        Assert.DoesNotContain("clrTypeName", diff.UpdatedSnapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("valueGenerated", diff.UpdatedSnapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isConcurrencyToken", diff.UpdatedSnapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"maxLength\": null", diff.UpdatedSnapshotJson);
        Assert.DoesNotContain("\"isIdentity\": false", diff.UpdatedSnapshotJson);

        // Idempotent re-diff produces zero changes
        var second = StandaloneSchemaDiffer.GenerateDiff(schema, diff.UpdatedSnapshotJson, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
        Assert.False(second.HasChanges);

        // Dialect mismatch throws
        Assert.Throws<InvalidOperationException>(() =>
            StandaloneSchemaDiffer.GenerateDiff(schema, diff.UpdatedSnapshotJson, SqlDialect.Oracle, interactiveRenamePrompt: false));
    }

    [Fact]
    public void DdlOrdering_FixesFkBeforeDropTable_DropBeforeRenameColumn_AndBaseBeforeComputed()
    {
        var oldModel = new DatabaseModel
        {
            Tables =
            [
                new TableSchema
                {
                    EntityName = "Coupon",
                    TableName = "coupons",
                    Columns = [new ColumnSchema { PropertyName = "Id", ColumnName = "id", SqlType = "bigint" }]
                },
                new TableSchema
                {
                    EntityName = "Order",
                    TableName = "orders",
                    Columns =
                    [
                        new ColumnSchema { PropertyName = "Id", ColumnName = "id", SqlType = "bigint" },
                        new ColumnSchema { PropertyName = "OldCode", ColumnName = "code", SqlType = "text" },
                        new ColumnSchema { PropertyName = "NewCode", ColumnName = "new_code", SqlType = "text" }
                    ],
                    ForeignKeys =
                    [
                        new ForeignKeySchema
                        {
                            Name = "fk_orders_coupons",
                            DependentColumns = ["id"],
                            PrincipalTable = "coupons",
                            PrincipalColumns = ["id"]
                        }
                    ]
                }
            ]
        };

        var newModel = new DatabaseModel
        {
            Tables =
            [
                new TableSchema
                {
                    EntityName = "Order",
                    TableName = "orders",
                    Schema = "sales",
                    Columns =
                    [
                        new ColumnSchema { PropertyName = "Id", ColumnName = "id", SqlType = "bigint" },
                        new ColumnSchema { PropertyName = "NewCode", ColumnName = "code", SqlType = "text" },
                        new ColumnSchema { PropertyName = "Total", ColumnName = "total", SqlType = "numeric", ComputedColumnSql = "\"amount\" + \"tax\"", IsComputedStored = true },
                        new ColumnSchema { PropertyName = "Tax", ColumnName = "tax", SqlType = "numeric" }
                    ]
                }
            ]
        };

        var v1 = StandaloneSchemaDiffer.GenerateDiff(oldModel, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
        var v2 = StandaloneSchemaDiffer.GenerateDiff(newModel, v1.UpdatedSnapshotJson, SqlDialect.PostgreSql, interactiveRenamePrompt: false);

        var dropFkIdx = v2.SqlScript.IndexOf("DROP CONSTRAINT IF EXISTS \"fk_orders_coupons\"", StringComparison.Ordinal);
        var dropTableIdx = v2.SqlScript.IndexOf("DROP TABLE IF EXISTS \"coupons\"", StringComparison.Ordinal);
        var ensureSchemaIdx = v2.SqlScript.IndexOf("CREATE SCHEMA IF NOT EXISTS \"sales\"", StringComparison.Ordinal);
        var setSchemaIdx = v2.SqlScript.IndexOf("SET SCHEMA \"sales\"", StringComparison.Ordinal);
        var dropColIdx = v2.SqlScript.IndexOf("DROP COLUMN IF EXISTS \"code\"", StringComparison.Ordinal);
        var renameColIdx = v2.SqlScript.IndexOf("RENAME COLUMN \"new_code\" TO \"code\"", StringComparison.Ordinal);
        var addTaxIdx = v2.SqlScript.IndexOf("ADD COLUMN IF NOT EXISTS \"tax\"", StringComparison.Ordinal);
        var addTotalIdx = v2.SqlScript.IndexOf("ADD COLUMN IF NOT EXISTS \"total\"", StringComparison.Ordinal);

        Assert.True(dropFkIdx >= 0 && dropTableIdx > dropFkIdx);
        Assert.True(ensureSchemaIdx >= 0 && setSchemaIdx > ensureSchemaIdx);
        Assert.True(dropColIdx >= 0 && renameColIdx > dropColIdx);
        Assert.True(addTaxIdx >= 0 && addTotalIdx > addTaxIdx);
    }

    [Fact]
    public void PostgresAlterColumn_DropsDefaultBeforeType_AndSetsNotNullBeforeIdentity()
    {
        var generator = new PostgresDialectGenerator();
        var sql = generator.GenerateSql([
            new AlterColumnOp(null, "orders",
                new ColumnSchema { ColumnName = "status", SqlType = "varchar(20)", DefaultValueSql = "'pending'" },
                new ColumnSchema { ColumnName = "status", SqlType = "integer", DefaultValueSql = "0" }),
            new AlterColumnOp(null, "orders",
                new ColumnSchema { ColumnName = "id", SqlType = "bigint", IsNullable = true, IsIdentity = false },
                new ColumnSchema { ColumnName = "id", SqlType = "bigint", IsNullable = false, IsIdentity = true })
        ]);

        var dropDefaultIdx = sql.IndexOf("DROP DEFAULT;", StringComparison.Ordinal);
        var alterTypeIdx = sql.IndexOf("TYPE integer;", StringComparison.Ordinal);
        var setDefaultIdx = sql.IndexOf("SET DEFAULT (0);", StringComparison.Ordinal);
        Assert.True(dropDefaultIdx >= 0 && alterTypeIdx > dropDefaultIdx && setDefaultIdx > alterTypeIdx);

        var setNotNullIdx = sql.IndexOf("SET NOT NULL;", StringComparison.Ordinal);
        var addIdentityIdx = sql.IndexOf("ADD GENERATED BY DEFAULT AS IDENTITY;", StringComparison.Ordinal);
        Assert.True(setNotNullIdx >= 0 && addIdentityIdx > setNotNullIdx);
    }

    [Fact]
    public void OracleDialect_RenamesAreIdempotent_AddColumnUsesCatalogGuard_AndSetDefaultThrows()
    {
        var generator = new OracleDialectGenerator(OracleDbCompatibility.Oracle23);
        var sql = generator.GenerateSql([
            new RenameColumnOp(null, "USERS", "OLD_COL", "NEW_COL"),
            new AddColumnOp(null, "USERS", new ColumnSchema { ColumnName = "EXTRA", SqlType = "VARCHAR2(50)", IsNullable = true }),
            new AlterColumnOp(null, "USERS",
                new ColumnSchema { ColumnName = "CODE", SqlType = "VARCHAR2(50)", IsNullable = true },
                new ColumnSchema { ColumnName = "CODE", SqlType = "VARCHAR2(50)", IsNullable = false })
        ]);

        Assert.Contains("IF v_old > 0 AND v_new = 0 THEN", sql);
        Assert.DoesNotContain("ADD IF NOT EXISTS (", sql);
        Assert.Contains("EXECUTE IMMEDIATE 'ALTER TABLE \"USERS\" ADD (\"EXTRA\" VARCHAR2(50) NULL)'", sql);
        Assert.Contains("NULLABLE = 'Y'", sql);

        Assert.Throws<NotSupportedException>(() => generator.GenerateSql([
            new AddForeignKeyOp(null, "ORDERS", new ForeignKeySchema
            {
                Name = "FK_ORDERS_USERS",
                DependentColumns = ["USER_ID"],
                PrincipalTable = "USERS",
                PrincipalColumns = ["ID"],
                OnDelete = DbReferentialAction.SetDefault
            })
        ]));
    }
}