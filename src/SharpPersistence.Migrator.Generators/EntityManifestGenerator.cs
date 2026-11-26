using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPersistence.Migrator.Generators;

[Generator]
public sealed class EntityManifestGenerator : IIncrementalGenerator
{
    private readonly record struct ManifestEntry
    {
        public required string GroupFullName { get; init; }
        public required string EntityFullName { get; init; }
        public required bool IsPubliclyAccessible { get; init; }
        public required SourceLocationInfo? Location { get; init; }
    }

    private readonly record struct AssemblyManifestMeta
    {
        public required string? AssemblyName { get; init; }
        public required bool HasManifestAttribute { get; init; }
        public required bool HasDisableAttribute { get; init; }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var isDisabled = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(GeneratorConstants.DisablePropertyName, out var value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        var assemblyMeta = context.CompilationProvider
            .Select(static (compilation, _) => new AssemblyManifestMeta
            {
                AssemblyName = compilation.AssemblyName,
                HasManifestAttribute = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestAttributeMetadataName) is not null,
                HasDisableAttribute = GeneratorConstants.HasAssemblyAttribute(
                    compilation,
                    GeneratorConstants.DisableEntityDiscoveryAttributeMetadataName)
            });

        // Stream 1: [Entity] (O(1) ForAttributeWithMetadataName)
        var defaultEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.DefaultEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ExtractDefaultEntityEntry(ctx))
            .Where(static x => x.HasValue)
            .Select(static (x, _) => x!.Value)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<ManifestEntry>(arr));

        // Stream 2: [Entity<TGroup>] (O(1) ForAttributeWithMetadataName)
        var groupedEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.GenericEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ExtractGroupedEntityEntries(ctx))
            .Where(static arr => arr.Length > 0)
            .SelectMany(static (arr, _) => arr)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<ManifestEntry>(arr));

        var pipeline = assemblyMeta
            .Combine(defaultEntities)
            .Combine(groupedEntities)
            .Combine(isDisabled);

        context.RegisterSourceOutput(pipeline, static (spc, input) => EmitManifest(spc, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    private static ManifestEntry? ExtractDefaultEntityEntry(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
            ctx.TargetNode is not ClassDeclarationSyntax classDecl ||
            symbol.IsAbstract ||
            symbol.TypeParameters.Length > 0)
        {
            return null;
        }

        var isPublic = GeneratorConstants.IsPubliclyAccessible(symbol);
        return new ManifestEntry
        {
            GroupFullName = "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName,
            EntityFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsPubliclyAccessible = isPublic,
            Location = isPublic ? null : SourceLocationInfo.From(classDecl.Identifier.GetLocation())
        };
    }

    private static EquatableArray<ManifestEntry> ExtractGroupedEntityEntries(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
            ctx.TargetNode is not ClassDeclarationSyntax classDecl ||
            symbol.IsAbstract ||
            symbol.TypeParameters.Length > 0)
        {
            return EquatableArray<ManifestEntry>.Empty;
        }

        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<ManifestEntry>(ctx.Attributes.Length);
        var entityFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var entityIsPublic = GeneratorConstants.IsPubliclyAccessible(symbol);
        SourceLocationInfo? cachedLoc = null;

        foreach (var attr in ctx.Attributes)
        {
            if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } attrClass ||
                attrClass.TypeArguments[0] is not INamedTypeSymbol groupSymbol)
            {
                continue;
            }

            var isPublic = entityIsPublic && GeneratorConstants.IsPubliclyAccessible(groupSymbol);
            builder.Add(new ManifestEntry
            {
                GroupFullName = groupSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                EntityFullName = entityFullName,
                IsPubliclyAccessible = isPublic,
                Location = isPublic ? null : (cachedLoc ??= SourceLocationInfo.From(classDecl.Identifier.GetLocation()))
            });
        }

        return new EquatableArray<ManifestEntry>(builder.ToImmutable());
    }

    private static void EmitManifest(
        SourceProductionContext spc,
        AssemblyManifestMeta meta,
        EquatableArray<ManifestEntry> defaultEntries,
        EquatableArray<ManifestEntry> groupedEntries,
        bool isDisabled)
    {
        if (isDisabled || !meta.HasManifestAttribute || meta.HasDisableAttribute)
        {
            return;
        }

        if (defaultEntries.Length == 0 && groupedEntries.Length == 0)
        {
            return;
        }

        var entitiesByGroup = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        AddEntries(spc, defaultEntries, entitiesByGroup);
        AddEntries(spc, groupedEntries, entitiesByGroup);

        if (entitiesByGroup.Count == 0)
        {
            return;
        }

        var sanitizedAssembly = GeneratorConstants.SanitizeAssemblyName(meta.AssemblyName);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {GeneratorConstants.GeneratedNamespace};");
        sb.AppendLine();
        sb.AppendLine($"[global::{GeneratorConstants.EntityManifestAttributeMetadataName}]");

        foreach (var kv in entitiesByGroup.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var entityTypeOfs = string.Join(", ", kv.Value
                .OrderBy(e => e, StringComparer.Ordinal)
                .Select(e => $"typeof({e})"));
            sb.AppendLine(
                $"[global::{GeneratorConstants.EntityManifestEntryAttributeMetadataName}(typeof({kv.Key}), {entityTypeOfs})]");
        }

        sb.AppendLine($"public static class {GeneratorConstants.ManifestPrefix}{sanitizedAssembly}");
        sb.AppendLine("{");
        sb.AppendLine("}");

        spc.AddSource("SharpPersistence.Migrator.EntityManifest.g.cs", sb.ToString());
    }

    private static void AddEntries(
        SourceProductionContext spc,
        EquatableArray<ManifestEntry> entries,
        Dictionary<string, HashSet<string>> entitiesByGroup)
    {
        foreach (var entry in entries)
        {
            if (!entry.IsPubliclyAccessible)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    GeneratorConstants.EntityOrMarkerMustBePublic,
                    entry.Location?.ToLocation() ?? Location.None,
                    GeneratorConstants.StripGlobalPrefix(entry.EntityFullName),
                    GeneratorConstants.StripGlobalPrefix(entry.GroupFullName)));
                continue;
            }

            if (!entitiesByGroup.TryGetValue(entry.GroupFullName, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                entitiesByGroup[entry.GroupFullName] = set;
            }

            set.Add(entry.EntityFullName);
        }
    }
}
