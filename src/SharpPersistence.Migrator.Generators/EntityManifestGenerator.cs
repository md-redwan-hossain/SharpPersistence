using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPersistence.Migrator.Generators;

[Generator]
public sealed class EntityManifestGenerator : IIncrementalGenerator
{
    internal sealed record ManifestEntry(
        string MarkerFullName,
        string EntityFullName,
        bool IsPubliclyAccessible,
        Location? Location);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var isDisabled = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(GeneratorConstants.DisablePropertyName, out var value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        // Lightweight, value-equatable assembly metadata (avoids carrying full Compilation into RegisterSourceOutput)
        var assemblyMeta = context.CompilationProvider
            .Select(static (compilation, _) => (
                AssemblyName: compilation.AssemblyName,
                HasManifestAttribute: compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestAttributeMetadataName) is not null,
                HasDisableAttribute: GeneratorConstants.HasAssemblyAttribute(
                    compilation,
                    GeneratorConstants.DisableEntityDiscoveryAttributeMetadataName)));

        // Stream 1: Built-in [Entity] attribute (O(1) ForAttributeWithMetadataName)
        var defaultEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.DefaultEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
                        ctx.TargetNode is not ClassDeclarationSyntax classDecl ||
                        symbol.IsAbstract ||
                        symbol.TypeParameters.Length > 0)
                    {
                        return null;
                    }

                    var markerName = "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName;
                    var entityName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    return new ManifestEntry(
                        markerName,
                        entityName,
                        GeneratorConstants.IsPubliclyAccessible(symbol),
                        classDecl.Identifier.GetLocation());
                })
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        // Stream 2: Custom [assembly: DiscoverEntitiesBy<TMarkerAttribute>] — value-equatable records extracted in transform
        var customEntities = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => ExtractCustomEntityEntries(ctx))
            .Where(static arr => !arr.IsDefaultOrEmpty)
            .SelectMany(static (arr, _) => arr)
            .Collect();

        var pipeline = assemblyMeta
            .Combine(defaultEntities)
            .Combine(customEntities)
            .Combine(isDisabled);

        context.RegisterSourceOutput(pipeline, static (spc, input) =>
        {
            if (input.Right)
            {
                return;
            }

            var meta = input.Left.Left.Left;
            if (!meta.HasManifestAttribute || meta.HasDisableAttribute)
            {
                return;
            }

            var allEntries = input.Left.Left.Right.AddRange(input.Left.Right);
            if (allEntries.Length == 0)
            {
                return;
            }

            var entitiesByMarker = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var entry in allEntries)
            {
                if (!entry.IsPubliclyAccessible)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        GeneratorConstants.EntityOrMarkerMustBePublic,
                        entry.Location,
                        GeneratorConstants.StripGlobalPrefix(entry.EntityFullName),
                        GeneratorConstants.StripGlobalPrefix(entry.MarkerFullName)));
                    continue;
                }

                if (!entitiesByMarker.TryGetValue(entry.MarkerFullName, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    entitiesByMarker[entry.MarkerFullName] = set;
                }

                set.Add(entry.EntityFullName);
            }

            if (entitiesByMarker.Count == 0)
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

            foreach (var kv in entitiesByMarker.OrderBy(e => e.Key, StringComparer.Ordinal))
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
        });
    }

    internal static ImmutableArray<ManifestEntry> ExtractCustomEntityEntries(GeneratorSyntaxContext ctx)
    {
        var classDecl = (ClassDeclarationSyntax)ctx.Node;
        var compilation = ctx.SemanticModel.Compilation;

        var customMarkers = GetAssemblyDiscoverMarkers(compilation);
        if (customMarkers.Count == 0)
        {
            return ImmutableArray<ManifestEntry>.Empty;
        }

        // Syntax-only pre-filter: reject 99% of attributed classes before calling GetDeclaredSymbol
        var markerSimpleNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var marker in customMarkers)
        {
            markerSimpleNames.Add(GeneratorConstants.GetSimpleAttributeName(marker.Name));
        }

        var hasSyntaxMatch = false;
        foreach (var list in classDecl.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                if (markerSimpleNames.Contains(GeneratorConstants.GetAttributeSyntaxSimpleName(attr.Name)))
                {
                    hasSyntaxMatch = true;
                    break;
                }
            }

            if (hasSyntaxMatch)
            {
                break;
            }
        }

        if (!hasSyntaxMatch)
        {
            return ImmutableArray<ManifestEntry>.Empty;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol ||
            symbol.IsAbstract ||
            symbol.TypeParameters.Length > 0)
        {
            return ImmutableArray<ManifestEntry>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ManifestEntry>();
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
            {
                continue;
            }

            foreach (var marker in customMarkers)
            {
                if (SymbolEqualityComparer.Default.Equals(attrClass, marker))
                {
                    var isPublic = GeneratorConstants.IsPubliclyAccessible(symbol) &&
                                   GeneratorConstants.IsPubliclyAccessible(marker);
                    builder.Add(new ManifestEntry(
                        marker.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        isPublic,
                        classDecl.Identifier.GetLocation()));
                }
            }
        }

        return builder.ToImmutable();
    }

    internal static List<INamedTypeSymbol> GetAssemblyDiscoverMarkers(Compilation compilation)
    {
        var discoverAttrDef = compilation.GetTypeByMetadataName(GeneratorConstants.DiscoverEntitiesByAttributeMetadataName);
        if (discoverAttrDef is null)
        {
            return [];
        }

        var result = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null ||
                !attrClass.IsGenericType ||
                !SymbolEqualityComparer.Default.Equals(attrClass.OriginalDefinition, discoverAttrDef))
            {
                continue;
            }

            if (attrClass.TypeArguments.Length == 1 &&
                attrClass.TypeArguments[0] is INamedTypeSymbol markerSymbol &&
                seen.Add(markerSymbol))
            {
                result.Add(markerSymbol);
            }
        }

        return result;
    }
}
