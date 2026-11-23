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
                b.HasForeignKey<Account>(x => x.AccountId)
                    .References(a => a.Id)
                    .IsUnique()
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
}
