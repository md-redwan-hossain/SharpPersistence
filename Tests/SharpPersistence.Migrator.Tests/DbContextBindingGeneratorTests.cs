using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using SharpPersistence.Abstractions.Attributes;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.EfCore;
using SharpPersistence.Migrator.EfCore.Attributes;
using SharpPersistence.Migrator.Generators;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Tests;

public sealed class DbContextBindingGeneratorTests
{
    private static readonly MetadataReference[] BaseReferences = BuildBaseReferences();

    private static MetadataReference[] BuildBaseReferences()
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];

        var refs = new List<MetadataReference>();
        foreach (var path in trustedAssemblies)
        {
            var fileName = Path.GetFileName(path);
            if (fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase))
            {
                refs.Add(MetadataReference.CreateFromFile(path));
            }
        }

        refs.Add(MetadataReference.CreateFromFile(typeof(EntityMarkerAttribute).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(IEntityRegistry).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(BindEntityRegistryAttribute<>).Assembly.Location));
        refs.Add(MetadataReference.CreateFromFile(typeof(DbContext).Assembly.Location));
        return [.. refs];
    }

    private static (CSharpCompilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
        RunGenerators(string assemblyName, string source, IEnumerable<MetadataReference>? extraRefs = null)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var implicitUsings = CSharpSyntaxTree.ParseText("global using System;", parseOptions);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var refs = extraRefs is null ? BaseReferences : BaseReferences.Concat(extraRefs);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [implicitUsings, tree],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        IIncrementalGenerator[] generators =
        [
            new EntityManifestGenerator(),
            new EntityRegistryGenerator(),
            new DbContextBindingGenerator()
        ];

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: generators.Select(GeneratorExtensions.AsSourceGenerator),
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var diagnostics,
            TestContext.Current.CancellationToken);

        return ((CSharpCompilation)outputCompilation, diagnostics, driver.GetRunResult());
    }

    [Fact]
    public void BindEntityRegistry_GeneratesDbSetsQueryablesCtorAndScopedOnModelCreating()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using SharpPersistence.Abstractions.Attributes;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.EfCore.Attributes;

            [assembly: DiscoverEntitiesBy<Billing.BillingEntityAttribute>]
            [assembly: DiscoverEntitiesBy<Billing.LookupEntityAttribute>]

            namespace Billing;

            public sealed class BillingEntityAttribute : EntityMarkerAttribute;
            public sealed class LookupEntityAttribute : EntityMarkerAttribute;

            [BillingEntity]
            public sealed class Invoice { public Guid Id { get; set; } }

            [BillingEntity]
            public sealed class AuditLog { public Guid Id { get; set; } }

            [BillingEntity]
            public sealed class OutboxMessage { public Guid Id { get; set; } }

            [BillingEntity]
            public sealed class PortfolioProduct { public Guid Id { get; set; } }

            [BillingEntity]
            public sealed class InventoryCategory { public Guid Id { get; set; } }

            [BillingEntity(ReadOnly = true)]
            public sealed class RefUser { public Guid Id { get; set; } }

            [LookupEntity]
            public sealed class FinancialCurrency { public Guid Id { get; set; } }

            [EntityRegistry<BillingEntityAttribute>]
            public sealed partial class BillingRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }

            [EntityRegistry<LookupEntityAttribute>]
            public sealed partial class LookupRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }

            [BindEntityRegistry<BillingRegistry>]
            [BindEntityRegistry<LookupRegistry>(ReadOnly = true)]
            [EntityRegistryItem<AuditLog>(ReadOnly = true)]
            [EntityRegistryItem<InventoryCategory>(Pluralize = false)]
            [EntityRegistryItem<OutboxMessage>(Exclude = true)]
            public sealed partial class BillingDbContext : DbContext
            {
                public DbSet<PortfolioProduct> ProductPortfolios => Set<PortfolioProduct>();
            }
            """;

        var (outputComp, diags, runResult) = RunGenerators("Billing", source);

        Assert.Empty(diags);
        Assert.Empty(outputComp.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));

        var bindingTree = runResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("Billing_BillingDbContext.DbContextBinding.g.cs", StringComparison.Ordinal));
        var generated = bindingTree.GetText(TestContext.Current.CancellationToken).ToString();

        // Generated constructor + scoped OnModelCreating + OnModelCreatingPartial
        Assert.Contains("public BillingDbContext(", generated);
        Assert.Contains("modelBuilder.ApplySchemaToEfCore(_sharpPersistenceSchema, _sharpPersistenceBoundEntityTypes);", generated);
        Assert.Contains("partial void OnModelCreatingPartial(ModelBuilder modelBuilder);", generated);

        // DbSet vs IQueryable precedence
        Assert.Contains("public DbSet<global::Billing.Invoice> Invoices => Set<global::Billing.Invoice>();", generated);
        Assert.Contains("public IQueryable<global::Billing.AuditLog> AuditLogs => Set<global::Billing.AuditLog>().AsNoTracking();", generated);
        Assert.Contains("public IQueryable<global::Billing.RefUser> RefUsers => Set<global::Billing.RefUser>().AsNoTracking();", generated);
        Assert.Contains("public IQueryable<global::Billing.FinancialCurrency> FinancialCurrencies => Set<global::Billing.FinancialCurrency>().AsNoTracking();", generated);

        // Pluralize = false override
        Assert.Contains("public DbSet<global::Billing.InventoryCategory> InventoryCategory => Set<global::Billing.InventoryCategory>();", generated);

        // Exclude = true omitted completely
        Assert.DoesNotContain("OutboxMessage", generated);

        // Manual property skipped from duplicate generation, but still in _boundEntityTypes
        Assert.DoesNotContain("PortfolioProducts", generated);
        Assert.Contains("typeof(global::Billing.PortfolioProduct)", generated);
    }

    [Fact]
    public void CustomCtorWithSchemaField_SkipsCtorAndGeneratesOnModelCreating()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using SharpPersistence.Abstractions.Attributes;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.EfCore.Attributes;
            using SharpPersistence.Migrator.Models;

            namespace CustomCtorDemo;

            [Entity]
            public sealed class Order { public Guid Id { get; set; } }

            [EntityRegistry]
            public sealed partial class AppRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }

            [BindEntityRegistry<AppRegistry>]
            public sealed partial class AppDbContext : DbContext
            {
                private readonly DatabaseModel _mySchema;

                public AppDbContext(DbContextOptions<AppDbContext> options, DatabaseModel schema, string tenant)
                    : base(options)
                {
                    _mySchema = schema;
                }
            }
            """;

        var (outputComp, diags, runResult) = RunGenerators("CustomCtorDemo", source);

        Assert.Empty(diags);
        Assert.Empty(outputComp.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));

        var bindingTree = runResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith(".DbContextBinding.g.cs", StringComparison.Ordinal));
        var generated = bindingTree.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.DoesNotContain("public AppDbContext(", generated);
        Assert.Contains("modelBuilder.ApplySchemaToEfCore(_mySchema, _sharpPersistenceBoundEntityTypes);", generated);
        Assert.Contains("public DbSet<global::CustomCtorDemo.Order> Orders => Set<global::CustomCtorDemo.Order>();", generated);
    }

    [Fact]
    public void Diagnostics_ReportsSpm008ThroughSpm013()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using SharpPersistence.Abstractions.Attributes;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.EfCore.Attributes;

            namespace DiagDbContext;

            [Entity]
            public sealed class Branch { public Guid Id { get; set; } }

            [Entity]
            public sealed class Order { public Guid Id { get; set; } }

            public sealed class UnboundEntity { public Guid Id { get; set; } }

            [EntityRegistry]
            public sealed partial class AppRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }

            // SPM008: not sealed, not partial
            [BindEntityRegistry<AppRegistry>]
            public class BadDbContext : DbContext { }

            // SPM011: EntityRegistryItem without BindEntityRegistry
            [EntityRegistryItem<Order>(ReadOnly = true)]
            public sealed partial class OrphanItemDbContext : DbContext { }

            // SPM009 + SPM010 + SPM011 + SPM012 + SPM013
            [BindEntityRegistry<AppRegistry>]
            [BindEntityRegistry<AppRegistry>]
            [EntityRegistryItem<Branch>(Exclude = true, ReadOnly = true)]
            [EntityRegistryItem<Order>(ReadOnly = true)]
            [EntityRegistryItem<Order>(ReadOnly = false)]
            [EntityRegistryItem<UnboundEntity>(ReadOnly = true)]
            public sealed partial class MultiDiagDbContext(DbContextOptions<MultiDiagDbContext> options)
                : DbContext(options)
            {
                public int Branches => 1;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    base.OnModelCreating(modelBuilder);
                }
            }
            """;

        var (_, diags, _) = RunGenerators("DiagDbContext", source);
        var ids = diags.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("SPM008", ids);
        Assert.Contains("SPM009", ids);
        Assert.Contains("SPM010", ids);
        Assert.Contains("SPM011", ids);
        Assert.Contains("SPM012", ids);
        Assert.Contains("SPM013", ids);
    }

    [Entity(ReadOnly = true)]
    private sealed class ReadOnlyProjectionStub
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ReadOnlyStubRegistry : IEntityRegistry
    {
        public void Configure(IEntityRegistryBuilder registry)
        {
            registry.Map<ReadOnlyProjectionStub>();
        }
    }

    [Fact]
    public void EntityMarkerReadOnly_AutomaticallySetsExcludeFromMigrationsInSchemaScanner()
    {
        var model = SchemaScanner.ScanRegistries(
            SqlDialect.PostgreSql,
            SqlIdentifierCase.LowerSnakeCase,
            registries: new ReadOnlyStubRegistry());

        var table = Assert.Single(model.Tables);
        Assert.True(table.ExcludeFromMigrations);
    }

    [Fact]
    public void InheritedBaseDbContextWithOnModelCreating_DoesNotReportSpm013()
    {
        const string source = """
            using Microsoft.EntityFrameworkCore;
            using SharpPersistence.Abstractions.Attributes;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.EfCore;
            using SharpPersistence.Migrator.EfCore.Attributes;
            using SharpPersistence.Migrator.Models;

            namespace BaseCtxDemo;

            [Entity]
            public sealed class Invoice { public Guid Id { get; set; } }

            [EntityRegistry]
            public sealed partial class BillingRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }

            public abstract class AppBaseDbContext<TContext>(DbContextOptions<TContext> options, DatabaseModel schema)
                : DbContext(options) where TContext : DbContext
            {
                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    base.OnModelCreating(modelBuilder);
                    modelBuilder.ApplySchemaToEfCore(schema);
                }
            }

            [BindEntityRegistry<BillingRegistry>]
            public sealed partial class BillingDbContext(DbContextOptions<BillingDbContext> options, DatabaseModel schema)
                : AppBaseDbContext<BillingDbContext>(options, schema);
            """;

        var (outputComp, diags, _) = RunGenerators("BaseCtxDemo", source);

        Assert.Empty(diags);
        Assert.Empty(outputComp.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }
}
