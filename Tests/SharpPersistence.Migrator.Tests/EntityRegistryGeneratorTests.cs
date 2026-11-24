using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpPersistence.Abstractions;
using SharpPersistence.Abstractions.Attributes;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Generators;

namespace SharpPersistence.Migrator.Tests;

public sealed class EntityRegistryGeneratorTests
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
        return [.. refs];
    }

    private sealed class DictOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
            values.TryGetValue(key, out value);
    }

    private sealed class DictOptionsProvider(Dictionary<string, string> globalValues) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global = new DictOptions(globalValues);
        private readonly AnalyzerConfigOptions _empty = new DictOptions([]);

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _empty;
    }

    private static (CSharpCompilation OutputCompilation, ImmutableArray<Diagnostic> Diagnostics, GeneratorDriverRunResult RunResult)
        RunGenerators(
            string assemblyName,
            string source,
            IEnumerable<MetadataReference>? extraRefs = null,
            Dictionary<string, string>? buildProps = null)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var implicitUsingsTree = CSharpSyntaxTree.ParseText("global using System;", parseOptions);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var refs = extraRefs is null ? BaseReferences : BaseReferences.Concat(extraRefs);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [implicitUsingsTree, tree],
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        IIncrementalGenerator[] generators = [new EntityManifestGenerator(), new EntityRegistryGenerator()];
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: generators.Select(GeneratorExtensions.AsSourceGenerator),
            parseOptions: parseOptions,
            optionsProvider: buildProps is not null ? new DictOptionsProvider(buildProps) : null);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        return ((CSharpCompilation)outputCompilation, diagnostics, driver.GetRunResult());
    }

    [Fact]
    public void BuiltInEntityAttribute_SingleProject_GeneratesExplicitInterfaceConfigureAndPairsConfig()
    {
        const string source = """
            using SharpPersistence.Abstractions;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.Builders;

            namespace DemoApp;

            [Entity]
            public sealed class Role
            {
                public Guid Id { get; set; }
            }

            [Entity]
            public sealed class UserRole
            {
                public Guid Id { get; set; }
            }

            [EntityConfiguration<Role>]
            public sealed class RoleConfiguration : IEntityConfiguration<Role>
            {
                public void Configure(EntityBuilder<Role> builder) => builder.ToTable("roles");
            }

            [EntityRegistry]
            public sealed partial class AppRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry)
                {
                    registry.HasDefaultSchema("public");
                }
            }
            """;

        var (outputComp, diags, runResult) = RunGenerators("DemoApp", source);

        Assert.Empty(diags);
        Assert.Empty(outputComp.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var registryTree = runResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("DemoApp_AppRegistry.g.cs", StringComparison.Ordinal));
        var registryGen = registryTree.GetText(TestContext.Current.CancellationToken).ToString();

        Assert.Contains("void global::SharpPersistence.Migrator.Abstractions.IEntityRegistry.Configure(", registryGen);
        Assert.Contains("registry.Map<global::DemoApp.Role, global::DemoApp.RoleConfiguration>();", registryGen);
        Assert.Contains("registry.Map<global::DemoApp.UserRole>();", registryGen);
        Assert.Contains("Configure(registry);", registryGen);
    }

    [Fact]
    public void CrossAssembly_CustomMarker_DomainToInfrastructureToRoot_IsolatesModulesAndEmitsRootExtension()
    {
        const string aclDomainSource = """
            using SharpPersistence.Abstractions;

            [assembly: DiscoverEntitiesBy<AccessControl.Domain.AccessControlEntityAttribute>]

            namespace AccessControl.Domain;

            public sealed class AccessControlEntityAttribute : EntityMarkerAttribute;

            [AccessControlEntity]
            public sealed class Role { public Guid Id { get; set; } }

            [AccessControlEntity]
            public sealed class UserRole { public Guid Id { get; set; } }
            """;

        const string billingDomainSource = """
            using SharpPersistence.Abstractions;

            [assembly: DiscoverEntitiesBy<Billing.Domain.BillingEntityAttribute>]

            namespace Billing.Domain;

            public sealed class BillingEntityAttribute : EntityMarkerAttribute;

            [BillingEntity]
            public sealed class Invoice { public Guid Id { get; set; } }
            """;

        var (aclDomainComp, aclDomainDiags, _) = RunGenerators("AccessControl.Domain", aclDomainSource);
        Assert.Empty(aclDomainDiags);
        Assert.Empty(aclDomainComp.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var (billingDomainComp, billingDomainDiags, _) = RunGenerators("Billing.Domain", billingDomainSource);
        Assert.Empty(billingDomainDiags);
        Assert.Empty(billingDomainComp.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        // AccessControl.Infrastructure references BOTH Domain assemblies (e.g. for cross-module FKs)
        // and must only pick up AccessControlEntityAttribute entities.
        const string aclInfraSource = """
            using AccessControl.Domain;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.Builders;

            namespace AccessControl.Infrastructure;

            [EntityConfiguration<Role>]
            public sealed class RoleConfiguration : IEntityConfiguration<Role>
            {
                public void Configure(EntityBuilder<Role> builder) => builder.ToTable("roles");
            }

            [EntityRegistry<AccessControlEntityAttribute>]
            public sealed partial class AccessControlRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry)
                {
                    registry.HasDefaultSchema("acl");
                }
            }
            """;

        var (aclInfraComp, aclInfraDiags, aclInfraResult) = RunGenerators(
            "AccessControl.Infrastructure",
            aclInfraSource,
            extraRefs: [aclDomainComp.ToMetadataReference(), billingDomainComp.ToMetadataReference()]);

        Assert.Empty(aclInfraDiags);
        Assert.Empty(aclInfraComp.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var aclRegistryText = aclInfraResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("AccessControlRegistry.g.cs", StringComparison.Ordinal))
            .GetText(TestContext.Current.CancellationToken)
            .ToString();

        Assert.Contains("registry.Map<global::AccessControl.Domain.Role, global::AccessControl.Infrastructure.RoleConfiguration>();", aclRegistryText);
        Assert.Contains("registry.Map<global::AccessControl.Domain.UserRole>();", aclRegistryText);
        Assert.DoesNotContain("Invoice", aclRegistryText);

        // Root startup project via type-safe [assembly: EntityRegistryRoot]
        const string rootSource = """
            using SharpPersistence.Migrator.Attributes;

            [assembly: EntityRegistryRoot]

            namespace WebApi;
            public static class Marker { }
            """;

        var (rootComp, rootDiags, rootResult) = RunGenerators(
            "WebApi",
            rootSource,
            extraRefs: [aclDomainComp.ToMetadataReference(), billingDomainComp.ToMetadataReference(), aclInfraComp.ToMetadataReference()]);

        Assert.Empty(rootDiags);
        Assert.Empty(rootComp.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));

        var rootText = rootResult.GeneratedTrees
            .Single(t => t.FilePath.EndsWith("GeneratedEntityRegistries.g.cs", StringComparison.Ordinal))
            .GetText(TestContext.Current.CancellationToken)
            .ToString();

        Assert.Contains("AddGeneratedEntityRegistries(", rootText);
        Assert.Contains("builder.AddRegistry<global::AccessControl.Infrastructure.AccessControlRegistry>();", rootText);
    }

    [Fact]
    public void Diagnostics_ReportsSpm001ThroughSpm005()
    {
        const string source = """
            using SharpPersistence.Abstractions;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.Builders;

            namespace DiagDemo;

            [Entity]
            public sealed class Item { public Guid Id { get; set; } }

            [Entity]
            public sealed class Other { public Guid Id { get; set; } }

            // SPM001: not sealed / not partial
            [EntityRegistry]
            public class BadRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }

            // SPM002: not implementing IEntityConfiguration<Other>
            [EntityConfiguration<Other>]
            public sealed class BadConfig { }

            // SPM003: duplicate configs for Item
            [EntityConfiguration<Item>]
            public sealed class ItemConfigA : IEntityConfiguration<Item>
            {
                public void Configure(EntityBuilder<Item> builder) { }
            }

            [EntityConfiguration<Item>]
            public sealed class ItemConfigB : IEntityConfiguration<Item>
            {
                public void Configure(EntityBuilder<Item> builder) { }
            }

            // SPM004: missing public void Configure(IEntityRegistryBuilder)
            [EntityRegistry]
            public sealed partial class MissingConfigureRegistry : IEntityRegistry
            {
                void IEntityRegistry.Configure(IEntityRegistryBuilder registry) { }
            }

            // SPM005: two valid registries matching the same [Entity]
            [EntityRegistry]
            public sealed partial class FirstRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }

            [EntityRegistry]
            public sealed partial class SecondRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }
            """;

        var (_, diags, _) = RunGenerators("DiagDemo", source);
        var ids = diags.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("SPM001", ids);
        Assert.Contains("SPM002", ids);
        Assert.Contains("SPM003", ids);
        Assert.Contains("SPM004", ids);
        Assert.Contains("SPM005", ids);
    }

    [Fact]
    public void Diagnostics_ReportsSpm006ForNonPublicEntityAndSpm007ForOrphanConfig()
    {
        const string source = """
            using SharpPersistence.Abstractions;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;
            using SharpPersistence.Migrator.Builders;

            namespace DiagExtra;

            // SPM006: internal entity cannot be emitted into public EntityManifest
            [Entity]
            internal sealed class InternalEntity { public Guid Id { get; set; } }

            // Public entity WITHOUT [Entity] attribute
            public sealed class UnmarkedEntity { public Guid Id { get; set; } }

            // SPM007: orphan [EntityConfiguration<UnmarkedEntity>] not matched by any registry
            [EntityConfiguration<UnmarkedEntity>]
            public sealed class UnmarkedConfig : IEntityConfiguration<UnmarkedEntity>
            {
                public void Configure(EntityBuilder<UnmarkedEntity> builder) { }
            }

            [EntityRegistry]
            public sealed partial class AppRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }
            """;

        var (_, diags, _) = RunGenerators("DiagExtra", source);
        var ids = diags.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("SPM006", ids);
        Assert.Contains("SPM007", ids);
    }

    [Fact]
    public void DisableEntityDiscovery_AssemblyAttributeAndMsBuildProp_SkipsAllGeneration()
    {
        const string source = """
            using SharpPersistence.Abstractions;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;

            namespace DisabledDemo;

            [Entity]
            public sealed class Role { public Guid Id { get; set; } }

            [EntityRegistry]
            public sealed partial class AppRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }
            """;

        var (_, propDiags, propResult) = RunGenerators(
            "DisabledDemo",
            source,
            buildProps: new Dictionary<string, string>
            {
                ["build_property.DisableEntityDiscovery"] = "true",
                ["build_property.EntityRegistryRoot"] = "true"
            });

        Assert.Empty(propDiags);
        Assert.Empty(propResult.GeneratedTrees);

        const string attrDisabledSource = """
            using SharpPersistence.Abstractions;
            using SharpPersistence.Migrator.Abstractions;
            using SharpPersistence.Migrator.Attributes;

            [assembly: DisableEntityDiscovery]
            [assembly: EntityRegistryRoot]

            namespace DisabledDemo;

            [Entity]
            public sealed class Role { public Guid Id { get; set; } }

            [EntityRegistry]
            public sealed partial class AppRegistry : IEntityRegistry
            {
                public void Configure(IEntityRegistryBuilder registry) { }
            }
            """;

        var (_, attrDiags, attrResult) = RunGenerators("DisabledDemoAttr", attrDisabledSource);
        Assert.Empty(attrDiags);
        Assert.Empty(attrResult.GeneratedTrees);
    }
}
