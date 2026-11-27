using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class EntityConventionTests
{
    private interface ICreateTracking
    {
        long CreatedByUserId { get; set; }
    }

    private interface IUpdateTracking
    {
        long? UpdatedByUserId { get; set; }
    }

    private interface ILookupEntity
    {
        int Id { get; set; }
        string Title { get; set; }
    }

    private sealed class User
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Invoice : ICreateTracking, IUpdateTracking
    {
        public long Id { get; set; }
        public long CreatedByUserId { get; set; }
        public long? UpdatedByUserId { get; set; }
    }

    private sealed class AuditLog : ICreateTracking
    {
        public long Id { get; set; }
        public long CreatedByUserId { get; set; }
    }

    private sealed class Currency : ILookupEntity
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }

    private sealed class ExcludedView
    {
        public string Code { get; set; } = string.Empty;
    }

    private sealed class BrokenComputedEntity
    {
        public int Id { get; set; }
        public decimal Total { get; set; }
    }

    private enum PaymentMethod
    {
        CashOnDelivery = 1,
        BankTransfer = 2
    }

    private sealed class IdentityRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<User>(_ => { });
        }
    }

    private sealed class BillingRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<Invoice>(_ => { });
            registry.Entity<AuditLog>(b =>
            {
                // Override global ICreateTracking Restrict convention for AuditLog only
                b.HasForeignKey(x => x.CreatedByUserId)
                    .References<User>(u => u.Id)
                    .OnDelete(DbReferentialAction.NoAction);
            });
            registry.Entity<Currency>(b =>
            {
                // Customize conventional index name without creating a duplicate index
                b.HasIndex(x => x.Title).HasDatabaseName("uq_custom_currency_title");
            });
        }
    }

    [Fact]
    public void ConfigureEntities_AppliesInterfaceFks_AllowsEntityOverride_AndSortsRegistries()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .PluralizeTableNames()
            .MapTableNames((entityType, defaultName) =>
                typeof(ILookupEntity).IsAssignableFrom(entityType)
                    ? $"lk_{defaultName}"
                    : defaultName)
            .ConfigureEntities<ICreateTracking>(e =>
                e.HasForeignKey(x => x.CreatedByUserId)
                    .References<User>(u => u.Id)
                    .OnDelete(DbReferentialAction.Restrict))
            .ConfigureEntities<IUpdateTracking>(e =>
                e.HasForeignKey(x => x.UpdatedByUserId)
                    .References<User>(u => u.Id)
                    .OnDelete(DbReferentialAction.SetNull))
            .ConfigureEntities<ILookupEntity>(e =>
            {
                e.HasSchema("lookup");
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasIndex(x => x.Title).IsUnique();
            })
            .AddRegistry<BillingRegistry>()
            .AddRegistry<IdentityRegistry>();

        var options = optionsBuilder.Build();

        // Topological sort must place IdentityRegistry before BillingRegistry due to conventional FKs
        var sorted = RegistryDependencyResolver.SortTopologically(
            options.Registries,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention);

        Assert.Equal([nameof(IdentityRegistry), nameof(BillingRegistry)], sorted.Select(r => r.GetType().Name).ToArray());

        var schema = SchemaScanner.ScanRegistries(
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PostgreSql.Enums,
            options.SqlExpressionOptions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention,
            options.EntityReplacements,
            [.. options.Registries]);

        var invoice = schema.Tables.Single(t => t.TableName == "invoices");
        Assert.Equal(2, invoice.ForeignKeys.Count);
        var createdFk = invoice.ForeignKeys.Single(fk => fk.DependentProperties.SequenceEqual([nameof(Invoice.CreatedByUserId)]));
        Assert.Equal(DbReferentialAction.Restrict, createdFk.OnDelete);
        Assert.Equal("users", createdFk.PrincipalTable);

        var updatedFk = invoice.ForeignKeys.Single(fk => fk.DependentProperties.SequenceEqual([nameof(Invoice.UpdatedByUserId)]));
        Assert.Equal(DbReferentialAction.SetNull, updatedFk.OnDelete);

        // AuditLog overrode ICreateTracking to NoAction with no duplicate FK
        var auditLog = schema.Tables.Single(t => t.TableName == "audit_logs");
        var auditFk = Assert.Single(auditLog.ForeignKeys);
        Assert.Equal(DbReferentialAction.NoAction, auditFk.OnDelete);

        // Currency got lk_ prefix, lookup schema, and single customized unique index
        var currency = schema.Tables.Single(t => t.TableName == "lk_currencies");
        Assert.Equal("lookup", currency.Schema);
        var titleIx = Assert.Single(currency.Indexes);
        Assert.True(titleIx.IsUnique);
        Assert.Equal("uq_custom_currency_title", titleIx.Name);
    }

    [Fact]
    public void PostgresEnum_UsesConfiguredNamingConvention()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .HasPostgresEnum<PaymentMethod>();
        optionsBuilder.UseNamingConvention(SqlIdentifierCase.PascalCase);

        var options = optionsBuilder.Build();
        var pgEnum = Assert.Single(options.PostgreSql.Enums);

        Assert.Equal("PaymentMethod", pgEnum.Name);
        Assert.Equal(["CashOnDelivery", "BankTransfer"], pgEnum.Labels);
    }

    private sealed class DoctorTestRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Entity<ExcludedView>(b => b.ExcludeFromMigrations());
            registry.Entity<BrokenComputedEntity>(b =>
            {
                b.Property(x => x.Total)
                    .HasComputedColumnSql(_ => throw new InvalidOperationException("broken computed expr"));
            });
        }
    }

    [Fact]
    public void DoctorEntityValidator_SkipsPkWarningForExcluded_AndCatchesBrokenComputedColumn()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .AddRegistry<DoctorTestRegistry>();

        var options = optionsBuilder.Build();
        var dialect = MigrationDialectRegistry.Get(options);
        var validator = new DoctorEntityValidator(options, dialect);

        var excludedDef = SchemaScanner.BuildSingleTableDefinition(
            typeof(ExcludedView),
            null,
            [obj => ((Builders.EntityBuilder<ExcludedView>)obj).ExcludeFromMigrations()],
            null,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.SqlExpressionOptions);

        var brokenDef = SchemaScanner.BuildSingleTableDefinition(
            typeof(BrokenComputedEntity),
            null,
            [obj => ((Builders.EntityBuilder<BrokenComputedEntity>)obj)
                .Property(x => x.Total)
                .HasComputedColumnSql(_ => throw new InvalidOperationException("broken computed expr"))],
            null,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.SqlExpressionOptions);

        var allTables = new Dictionary<Type, (string, Builders.TableDefinition)>
        {
            [typeof(ExcludedView)] = (nameof(DoctorTestRegistry), excludedDef),
            [typeof(BrokenComputedEntity)] = (nameof(DoctorTestRegistry), brokenDef)
        };
        var enumLookup = new Dictionary<string, PostgresEnumSchema>();

        var excludedItems = new List<DoctorCheckItem>();
        validator.ValidateTable(excludedDef, allTables, enumLookup, excludedItems);
        Assert.Empty(excludedItems);

        var brokenItems = new List<DoctorCheckItem>();
        validator.ValidateTable(brokenDef, allTables, enumLookup, brokenItems);
        Assert.Contains(brokenItems, i => i.Severity == DoctorSeverity.Error && i.Message.Contains("broken computed expr"));
    }
}
