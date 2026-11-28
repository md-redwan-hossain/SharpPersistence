using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class EntityTypeReplacementTests
{
    private interface ICreateTracking
    {
        long CreatedByUserId { get; set; }
    }

    private sealed class RefUser
    {
        public long Id { get; set; }
    }

    private sealed class RefUserAlias
    {
        public long Id { get; set; }
    }

    private sealed class UnregisteredOwner
    {
        public long Id { get; set; }
    }

    private sealed class User
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Role : ICreateTracking
    {
        public long Id { get; set; }
        public long CreatedByUserId { get; set; }
    }

    private sealed class Invoice : ICreateTracking
    {
        public long Id { get; set; }
        public long CreatedByUserId { get; set; }
    }

    private sealed class RefUserConfig : IEntityConfiguration<RefUser>
    {
        public void Configure(EntityBuilder<RefUser> builder)
        {
            builder.ToTable("users", "access_control");
            builder.ExcludeFromMigrations();
        }
    }

    private sealed class AccessControlRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.HasDefaultSchema("access_control");
            registry.Map<User>();
            registry.Map<Role>();
        }
    }

    private sealed class BillingRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.HasDefaultSchema("billing");
            registry.Map<Invoice>();
        }
    }

    private sealed class CommonStubRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Map<RefUser, RefUserConfig>();
        }
    }

    [Fact]
    public void ReplaceEntityTypes_ResolvesCrossAndOwningModuleFks_AndSortsRegistries()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .PluralizeTableNames()
            .ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, m =>
                m.Entity<ICreateTracking>(e =>
                    e.HasForeignKey(x => x.CreatedByUserId)
                        .References<RefUser>(u => u.Id)
                        .OnDelete(DbReferentialAction.Restrict)))
            .ReplaceEntityTypes(r => r.Replace<RefUser, User>())
            .AddRegistry<BillingRegistry>()
            .AddRegistry<AccessControlRegistry>();

        var options = optionsBuilder.Build();

        var sorted = RegistryDependencyResolver.SortTopologically(
            options.Registries,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention,
            options.EntityReplacements);

        Assert.Equal(
            [nameof(AccessControlRegistry), nameof(BillingRegistry)],
            sorted.Select(r => r.GetType().Name).ToArray());

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
        var invoiceFk = Assert.Single(invoice.ForeignKeys);
        Assert.Equal("fk_invoices_users_created_by_user_id", invoiceFk.Name);
        Assert.Equal("access_control", invoiceFk.PrincipalSchema);
        Assert.Equal("users", invoiceFk.PrincipalTable);
        Assert.Equal(typeof(User).FullName, invoiceFk.PrincipalEntityName);

        var role = schema.Tables.Single(t => t.TableName == "roles");
        var roleFk = Assert.Single(role.ForeignKeys);
        Assert.Equal("access_control", roleFk.PrincipalSchema);
        Assert.Equal("users", roleFk.PrincipalTable);
        Assert.Equal(typeof(User).FullName, roleFk.PrincipalEntityName);
    }

    [Fact]
    public void ExcludedExternalEntityFallback_OrdersOwningRegistryBeforeDependent_WithoutReplaceEntityTypes()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
            .PluralizeTableNames()
            .ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, m =>
                m.Entity<ICreateTracking>(e =>
                    e.HasForeignKey(x => x.CreatedByUserId)
                        .References<RefUser>(u => u.Id)
                        .OnDelete(DbReferentialAction.Restrict)))
            .AddRegistry<BillingRegistry>()
            .AddRegistry<CommonStubRegistry>()
            .AddRegistry<AccessControlRegistry>();

        var options = optionsBuilder.Build();

        var sorted = RegistryDependencyResolver.SortTopologically(
            options.Registries,
            options.Dialect,
            options.IdentifierCase,
            options.PropertyConventions,
            options.PluralizeTableNames,
            options.EntityConventions,
            options.TableNameConvention,
            options.EntityReplacements);

        var names = sorted.Select(r => r.GetType().Name).ToList();
        Assert.True(names.IndexOf(nameof(AccessControlRegistry)) < names.IndexOf(nameof(BillingRegistry)));
    }

    [Fact]
    public void EntityTypeReplacementBuilder_ThrowsWhenReplacingTypeWithItself()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        Assert.Throws<ArgumentException>(() =>
            optionsBuilder.ReplaceEntityTypes(r => r.Replace<User, User>()));
    }

    [Fact]
    public void DoctorCrossRegistryValidator_FlagsChainedUnregisteredAndExcludedReplacementTargets()
    {
        var optionsBuilder = new DbMigratorOptionsBuilder();
        optionsBuilder
            .UsePostgreSql("Host=localhost;Database=test")
            .ReplaceEntityTypes(r => r
                .Replace<RefUserAlias, RefUser>()
                .Replace<RefUser, UnregisteredOwner>())
            .AddRegistry<AccessControlRegistry>()
            .AddRegistry<CommonStubRegistry>();

        var options = optionsBuilder.Build();
        var dialect = MigrationDialectRegistry.Get(options);
        var validator = new DoctorCrossRegistryValidator(options, dialect);

        var userDef = SchemaScanner.BuildSingleTableDefinition(
            typeof(User), null, "access_control",
            options.Dialect, options.IdentifierCase, options.PropertyConventions, options.SqlExpressionOptions);
        var refUserDef = SchemaScanner.BuildSingleTableDefinition(
            typeof(RefUser), typeof(RefUserConfig), null,
            options.Dialect, options.IdentifierCase, options.PropertyConventions, options.SqlExpressionOptions);

        var allTables = new Dictionary<Type, (string, TableDefinition)>
        {
            [typeof(User)] = (nameof(AccessControlRegistry), userDef),
            [typeof(RefUser)] = (nameof(CommonStubRegistry), refUserDef)
        };

        var checks = new List<DoctorCheckItem>();
        validator.Validate(allTables, checks, hasRegistryErrors: true);

        Assert.Contains(checks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("itself registered as a replaced entity"));
        Assert.Contains(checks, c => c.Severity == DoctorSeverity.Error && c.Message.Contains("UnregisteredOwner"));
    }

    [Fact]
    public async Task MigrationScriptStore_DiscoverScripts_RespectsEntityReplacementsAndConventionsOrder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"sp_stub_scripts_{Guid.NewGuid():N}");
        try
        {
            var accessDir = Path.Combine(tempRoot, DbMigratorConsts.ScriptsDirectory, nameof(AccessControlRegistry));
            var billingDir = Path.Combine(tempRoot, DbMigratorConsts.ScriptsDirectory, nameof(BillingRegistry));
            Directory.CreateDirectory(accessDir);
            Directory.CreateDirectory(billingDir);

            File.WriteAllText(Path.Combine(accessDir, "20261124000001_Initial.sql"), "BEGIN;\nCOMMIT;\n");
            File.WriteAllText(Path.Combine(billingDir, "20261124000001_Initial.sql"), "BEGIN;\nCOMMIT;\n");

            var optionsBuilder = new DbMigratorOptionsBuilder();
            optionsBuilder
                .UsePostgreSql("Host=localhost;Database=test")
                .UseRootPath(tempRoot)
                .UseNamingConvention(SqlIdentifierCase.LowerSnakeCase)
                .PluralizeTableNames()
                .ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, m =>
                    m.Entity<ICreateTracking>(e =>
                        e.HasForeignKey(x => x.CreatedByUserId)
                            .References<RefUser>(u => u.Id)
                            .OnDelete(DbReferentialAction.Restrict)))
                .ReplaceEntityTypes(r => r.Replace<RefUser, User>())
                .AddRegistry<BillingRegistry>()
                .AddRegistry<AccessControlRegistry>();

            var options = optionsBuilder.Build();
            var store = new MigrationScriptStore(options);
            await new MigrationRunlistManager(options, store).SyncAsync(ct: TestContext.Current.CancellationToken);
            var discovered = store.DiscoverScripts();

            Assert.Equal(
                [nameof(AccessControlRegistry), nameof(BillingRegistry)],
                discovered.Select(d => d.RegistryName).ToArray());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
