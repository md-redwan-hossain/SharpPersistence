using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpPersistence.Abstractions.Generators;

internal sealed record SourceLocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static SourceLocationInfo? From(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new SourceLocationInfo(
            lineSpan.Path ?? string.Empty,
            location.SourceSpan,
            lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
}

internal static class EntityIndexGeneratorConstants
{
    public const string DisablePropertyName = "build_property.DisableEntityDiscovery";

    public const string GeneratedNamespace = "SharpPersistence.Migrator.Generated";
    public const string EntityIndexPrefix = "EntityIndex_";

    public const string DefaultEntityAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.EntityAttribute";
    public const string GenericEntityAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.EntityAttribute`1";
    public const string DisableEntityDiscoveryAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.DisableEntityDiscoveryAttribute";
    public const string EntityIndexAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.EntityIndexAttribute";

    public const string DiagnosticCategory = "SharpPersistence.Migrator";

    public static readonly DiagnosticDescriptor EntityOrMarkerMustBePublic = new(
        id: "SPM006",
        title: "Discovered entity or marker attribute must be public",
        messageFormat: "Type '{0}' marked with '{1}' must be publicly accessible for compile-time entity index generation",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static string StripGlobalPrefix(string typeName) =>
        typeName.StartsWith("global::", StringComparison.Ordinal)
            ? typeName.Substring("global::".Length)
            : typeName;

    public static bool HasAssemblyAttribute(Compilation compilation, string attributeMetadataName)
    {
        var attrSymbol = compilation.GetTypeByMetadataName(attributeMetadataName);
        if (attrSymbol is null)
        {
            return false;
        }

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSymbol))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPubliclyAccessible(INamedTypeSymbol symbol)
    {
        INamedTypeSymbol? current = symbol;
        while (current is not null)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }

            current = current.ContainingType;
        }

        return true;
    }

    public static string SanitizeAssemblyName(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return "Unknown";
        }

        var sb = new StringBuilder(assemblyName!.Length);
        for (var i = 0; i < assemblyName.Length; i++)
        {
            var c = assemblyName[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                if (i == 0 && char.IsDigit(c))
                {
                    sb.Append('_');
                }

                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.Length == 0 ? "Unknown" : sb.ToString();
    }
}
