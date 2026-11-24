using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Enums;
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

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
        Assert.True(diff.HasChanges);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS", diff.SqlScript);

        var secondDiff = StandaloneSchemaDiffer.GenerateDiff(schema, diff.UpdatedSnapshotJson, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
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

        var diff = StandaloneSchemaDiffer.GenerateDiff(schema, null, SqlDialect.PostgreSql, interactiveRenamePrompt: false);
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
}
