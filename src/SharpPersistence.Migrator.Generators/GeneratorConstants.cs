using System.Text;
using Microsoft.CodeAnalysis;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.Generators;

internal static class GeneratorConstants
{
    public const string DisablePropertyName = "build_property.DisableEntityDiscovery";
    public const string RootPropertyName = "build_property.EntityRegistryRoot";

    public const string GeneratedNamespace = "SharpPersistence.Migrator.Generated";
    public const string ManifestPrefix = "EntityManifest_";
    public const string AggregatorPrefix = "EntityRegistryAggregator_";

    public const string EntityMarkerAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.EntityMarkerAttribute";
    public const string DefaultEntityAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.EntityAttribute";
    public const string DiscoverEntitiesByAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.DiscoverEntitiesByAttribute`1";
    public const string DisableEntityDiscoveryAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.DisableEntityDiscoveryAttribute";
    public const string EntityManifestAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.EntityManifestAttribute";
    public const string EntityManifestEntryAttributeMetadataName = "SharpPersistence.Abstractions.Attributes.EntityManifestEntryAttribute";

    public const string EntityRegistryAttributeMetadataName = "SharpPersistence.Migrator.Attributes.EntityRegistryAttribute";
    public const string GenericEntityRegistryAttributeMetadataName = "SharpPersistence.Migrator.Attributes.EntityRegistryAttribute`1";
    public const string EntityConfigurationAttributeMetadataName = "SharpPersistence.Migrator.Attributes.EntityConfigurationAttribute`1";
    public const string EntityRegistryRootAttributeMetadataName = "SharpPersistence.Migrator.Attributes.EntityRegistryRootAttribute";
    public const string EntityRegistryAggregatorAttributeMetadataName = "SharpPersistence.Migrator.Attributes.EntityRegistryAggregatorAttribute";
    public const string EntityRegistryAggregatorEntryAttributeMetadataName = "SharpPersistence.Migrator.Attributes.EntityRegistryAggregatorEntryAttribute";

    public const string IEntityRegistryMetadataName = "SharpPersistence.Migrator.Abstractions.IEntityRegistry";
    public const string IEntityRegistryBuilderMetadataName = "SharpPersistence.Migrator.Abstractions.IEntityRegistryBuilder";
    public const string IEntityConfigurationMetadataName = "SharpPersistence.Migrator.Abstractions.IEntityConfiguration`1";

    public const string BindEntityRegistryAttributeMetadataName = "SharpPersistence.Migrator.EfCore.Attributes.BindEntityRegistryAttribute`1";
    public const string EntityRegistryItemAttributeMetadataName = "SharpPersistence.Migrator.EfCore.Attributes.EntityRegistryItemAttribute`1";
    public const string DatabaseModelMetadataName = "SharpPersistence.Migrator.Models.DatabaseModel";
    public const string EfCoreDbContextMetadataName = "Microsoft.EntityFrameworkCore.DbContext";
    public const string EfCoreDbSetMetadataName = "Microsoft.EntityFrameworkCore.DbSet`1";
    public const string EfCoreModelBuilderMetadataName = "Microsoft.EntityFrameworkCore.ModelBuilder";
    public const string SystemQueryableMetadataName = "System.Linq.IQueryable`1";
    public const string EntityStubBuilderMetadataName = "SharpPersistence.Migrator.Configuration.EntityStubBuilder";
    public const string EntityBuilderMetadataName = "SharpPersistence.Migrator.Builders.EntityBuilder`1";
    public const string OwnedEntityBuilderMetadataName = "SharpPersistence.Migrator.Builders.OwnedEntityBuilder`2";
    public const string ColumnBuilderBaseMetadataName = "SharpPersistence.Migrator.Builders.ColumnBuilderBase`1";

    public const string DiagnosticCategory = "SharpPersistence.Migrator";

    public static readonly DiagnosticDescriptor RegistryMustBeSealedPartialImplementingInterface = new(
        id: DiagnosticIds.RegistryMustBeSealedPartialImplementingInterface,
        title: "Invalid [EntityRegistry] class declaration",
        messageFormat: "Type '{0}' decorated with [EntityRegistry] must be sealed, partial, and implement IEntityRegistry",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConfigurationInvalid = new(
        id: DiagnosticIds.ConfigurationInvalid,
        title: "Invalid [EntityConfiguration<TEntity>] class declaration",
        messageFormat: "Type '{0}' decorated with [EntityConfiguration<{1}>] must be sealed, have a public parameterless constructor, and implement IEntityConfiguration<{1}>",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateEntityConfiguration = new(
        id: DiagnosticIds.DuplicateEntityConfiguration,
        title: "Duplicate [EntityConfiguration<TEntity>] detected",
        messageFormat: "Multiple [EntityConfiguration] classes found for entity '{0}': {1}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RegistryMustDeclareConfigure = new(
        id: DiagnosticIds.RegistryMustDeclareConfigure,
        title: "Missing Configure(IEntityRegistryBuilder) method",
        messageFormat: "Type '{0}' decorated with [EntityRegistry] must declare 'public void Configure(IEntityRegistryBuilder registry)'",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateEntityAcrossRegistries = new(
        id: DiagnosticIds.DuplicateEntityAcrossRegistries,
        title: "Entity matched by multiple registries",
        messageFormat: "Entity '{0}' is matched by multiple [EntityRegistry] classes in this compilation: {1}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EntityOrMarkerMustBePublic = new(
        id: DiagnosticIds.EntityOrMarkerMustBePublic,
        title: "Discovered entity or marker attribute must be public",
        messageFormat: "Type '{0}' marked with '{1}' must be publicly accessible for compile-time entity manifest generation",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrphanEntityConfiguration = new(
        id: DiagnosticIds.OrphanEntityConfiguration,
        title: "Unpaired [EntityConfiguration<TEntity>]",
        messageFormat: "Configuration '{0}' targets entity '{1}', which is not matched by any [EntityRegistry] in this compilation",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DbContextMustBeSealedPartial = new(
        id: DiagnosticIds.DbContextMustBeSealedPartial,
        title: "Invalid [BindEntityRegistry<TRegistry>] class declaration",
        messageFormat: "Type '{0}' decorated with [BindEntityRegistry] must be sealed, partial, and inherit Microsoft.EntityFrameworkCore.DbContext",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidRegistryItemCombination = new(
        id: DiagnosticIds.InvalidRegistryItemCombination,
        title: "Invalid [EntityRegistryItem<TEntity>] property combination",
        messageFormat: "[EntityRegistryItem<{0}>] on '{1}' cannot set Exclude = true together with ReadOnly or Pluralize",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateDbContextBindingAttribute = new(
        id: DiagnosticIds.DuplicateDbContextBindingAttribute,
        title: "Duplicate DbContext registry binding attribute",
        messageFormat: "Duplicate {0} for '{1}' on '{2}'",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OrphanEntityRegistryItem = new(
        id: DiagnosticIds.OrphanEntityRegistryItem,
        title: "Unmatched [EntityRegistryItem<TEntity>]",
        messageFormat: "[EntityRegistryItem<{0}>] on '{1}' {2}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DbContextPropertyNameCollision = new(
        id: DiagnosticIds.DbContextPropertyNameCollision,
        title: "Generated DbContext property name collision",
        messageFormat: "Generated property '{0}' for entity '{1}' on '{2}' collides with {3}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DbContextSchemaNotWired = new(
        id: DiagnosticIds.DbContextMissingSchemaWiring,
        title: "DatabaseModel schema is not wired into EF Core",
        messageFormat: "DbContext '{0}' {1}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequiredColumnInsideOptionalOwnedNavigation = new(
        id: DiagnosticIds.RequiredColumnInsideOptionalOwnedNavigation,
        title: "IsRequired() cannot be used inside an optional OwnsOne navigation",
        messageFormat: "Cannot call IsRequired() inside optional OwnsOne navigation '{0}'; columns under a nullable owned navigation must remain nullable in the database",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidEntityStubMapping = new(
        id: DiagnosticIds.InvalidEntityStubMapping,
        title: "Invalid ConfigureEntityStubs mapping",
        messageFormat: "{0}",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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

    public static string StripGlobalPrefix(string fullTypeName) =>
        fullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? fullTypeName.Substring("global::".Length)
            : fullTypeName;

    public static bool IsPubliclyAccessible(INamedTypeSymbol symbol)
    {
        var current = symbol;
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

    public static string GetSimpleAttributeName(string metadataName)
    {
        var lastDot = metadataName.LastIndexOf('.');
        var name = lastDot >= 0 ? metadataName.Substring(lastDot + 1) : metadataName;
        var tick = name.IndexOf('`');
        if (tick >= 0)
        {
            name = name.Substring(0, tick);
        }

        return name.EndsWith("Attribute", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - "Attribute".Length)
            : name;
    }

    public static string GetAttributeSyntaxSimpleName(Microsoft.CodeAnalysis.CSharp.Syntax.NameSyntax nameSyntax)
    {
        var raw = nameSyntax switch
        {
            Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax id => id.Identifier.ValueText,
            Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
            _ => nameSyntax.ToString()
        };

        return raw.EndsWith("Attribute", StringComparison.Ordinal)
            ? raw.Substring(0, raw.Length - "Attribute".Length)
            : raw;
    }
}
