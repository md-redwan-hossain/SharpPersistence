using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPersistence.Migrator.Generators;

namespace SharpPersistence.Abstractions.Generators;

[Generator]
public sealed class EntityIndexGenerator : IIncrementalGenerator
{
    private readonly record struct IndexEntry
    {
        public required string GroupFullName { get; init; }
        public required string EntityFullName { get; init; }
        public required bool IsPubliclyAccessible { get; init; }
        public required SourceLocationInfo? Location { get; init; }
    }

    private readonly record struct AssemblyIndexMeta
    {
        public required string? AssemblyName { get; init; }
        public required bool HasIndexAttribute { get; init; }
        public required bool HasDisableAttribute { get; init; }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var isDisabled = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(EntityIndexGeneratorConstants.DisablePropertyName, out var value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        var assemblyMeta = context.CompilationProvider
            .Select(static (compilation, _) => new AssemblyIndexMeta
            {
                AssemblyName = compilation.AssemblyName,
                HasIndexAttribute = compilation.GetTypeByMetadataName(EntityIndexGeneratorConstants.EntityIndexAttributeMetadataName) is not null,
                HasDisableAttribute = EntityIndexGeneratorConstants.HasAssemblyAttribute(
                    compilation,
                    EntityIndexGeneratorConstants.DisableEntityDiscoveryAttributeMetadataName)
            });

        // Stream 1: [Entity] (O(1) ForAttributeWithMetadataName)
        var defaultEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EntityIndexGeneratorConstants.DefaultEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ExtractDefaultEntityEntry(ctx))
            .Where(static x => x.HasValue)
            .Select(static (x, _) => x!.Value)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<IndexEntry>(arr));

        // Stream 2: [Entity<TGroup>] (O(1) ForAttributeWithMetadataName)
        var groupedEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                EntityIndexGeneratorConstants.GenericEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ExtractGroupedEntityEntries(ctx))
            .Where(static arr => arr.Length > 0)
            .SelectMany(static (arr, _) => arr)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<IndexEntry>(arr));

        var pipeline = assemblyMeta
            .Combine(defaultEntities)
            .Combine(groupedEntities)
            .Combine(isDisabled);

        context.RegisterSourceOutput(pipeline, static (spc, input) => EmitEntityIndex(spc, input.Left.Left.Left, input.Left.Left.Right, input.Left.Right, input.Right));
    }

    private static IndexEntry? ExtractDefaultEntityEntry(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
            ctx.TargetNode is not ClassDeclarationSyntax classDecl ||
            symbol.IsAbstract ||
            symbol.TypeParameters.Length > 0)
        {
            return null;
        }

        var isPublic = EntityIndexGeneratorConstants.IsPubliclyAccessible(symbol);
        return new IndexEntry
        {
            GroupFullName = "global::" + EntityIndexGeneratorConstants.DefaultEntityAttributeMetadataName,
            EntityFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsPubliclyAccessible = isPublic,
            Location = isPublic ? null : SourceLocationInfo.From(classDecl.Identifier.GetLocation())
        };
    }

    private static EquatableArray<IndexEntry> ExtractGroupedEntityEntries(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
            ctx.TargetNode is not ClassDeclarationSyntax classDecl ||
            symbol.IsAbstract ||
            symbol.TypeParameters.Length > 0)
        {
            return EquatableArray<IndexEntry>.Empty;
        }

        var builder = System.Collections.Immutable.ImmutableArray.CreateBuilder<IndexEntry>(ctx.Attributes.Length);
        var entityFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var entityIsPublic = EntityIndexGeneratorConstants.IsPubliclyAccessible(symbol);
        SourceLocationInfo? cachedLoc = null;

        foreach (var attr in ctx.Attributes)
        {
            if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } attrClass ||
                attrClass.TypeArguments[0] is not INamedTypeSymbol groupSymbol)
            {
                continue;
            }

            var isPublic = entityIsPublic && EntityIndexGeneratorConstants.IsPubliclyAccessible(groupSymbol);
            builder.Add(new IndexEntry
            {
                GroupFullName = groupSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                EntityFullName = entityFullName,
                IsPubliclyAccessible = isPublic,
                Location = isPublic ? null : (cachedLoc ??= SourceLocationInfo.From(classDecl.Identifier.GetLocation()))
            });
        }

        return new EquatableArray<IndexEntry>(builder.ToImmutable());
    }

    private static void EmitEntityIndex(
        SourceProductionContext spc,
        AssemblyIndexMeta meta,
        EquatableArray<IndexEntry> defaultEntries,
        EquatableArray<IndexEntry> groupedEntries,
        bool isDisabled)
    {
        if (isDisabled || !meta.HasIndexAttribute || meta.HasDisableAttribute)
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

        var sanitizedAssembly = EntityIndexGeneratorConstants.SanitizeAssemblyName(meta.AssemblyName);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {EntityIndexGeneratorConstants.GeneratedNamespace};");
        sb.AppendLine();

        foreach (var kv in entitiesByGroup.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            var orderedEntities = kv.Value.OrderBy(e => e, StringComparer.Ordinal).ToList();
            sb.AppendLine($"[global::{EntityIndexGeneratorConstants.EntityIndexAttributeMetadataName}(");
            sb.AppendLine($"    typeof({kv.Key}),");
            for (var i = 0; i < orderedEntities.Count; i++)
            {
                var suffix = i < orderedEntities.Count - 1 ? "," : ")]";
                sb.AppendLine($"    typeof({orderedEntities[i]}){suffix}");
            }
        }

        sb.AppendLine($"public static class {EntityIndexGeneratorConstants.EntityIndexPrefix}{sanitizedAssembly}");
        sb.AppendLine("{");
        sb.AppendLine("}");

        spc.AddSource("SharpPersistence.Migrator.EntityIndex.g.cs", sb.ToString());
    }

    private static void AddEntries(
        SourceProductionContext spc,
        EquatableArray<IndexEntry> entries,
        Dictionary<string, HashSet<string>> entitiesByGroup)
    {
        foreach (var entry in entries)
        {
            if (!entry.IsPubliclyAccessible)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    EntityIndexGeneratorConstants.EntityOrMarkerMustBePublic,
                    entry.Location?.ToLocation() ?? Location.None,
                    EntityIndexGeneratorConstants.StripGlobalPrefix(entry.EntityFullName),
                    EntityIndexGeneratorConstants.StripGlobalPrefix(entry.GroupFullName)));
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
