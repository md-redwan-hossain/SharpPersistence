using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPersistence.Migrator.Generators;

internal static class DbContextBindingAnalyzer
{
    private sealed record BoundRegistryInput(
        INamedTypeSymbol RegistrySymbol,
        bool ReadOnly,
        bool Pluralize,
        SourceLocationInfo? Location);

    private sealed record ItemOverrideInput(
        INamedTypeSymbol EntitySymbol,
        string EntityFullName,
        bool HasReadOnly,
        bool ReadOnly,
        bool HasPluralize,
        bool Pluralize,
        bool Exclude,
        SourceLocationInfo? Location);

    private sealed record DiscoveredEntity(
        INamedTypeSymbol Symbol,
        string FullTypeName,
        string SimpleName,
        bool MarkedReadOnly);

    private sealed record ExistingMembersInfo(
        HashSet<string> EntityPropertyTypes,
        HashSet<string> MemberNames);

    private sealed record WiringResult(
        bool GenerateConstructor,
        bool GenerateOnModelCreating,
        string? ExistingSchemaMemberName);

    public static EquatableArray<PendingDiagnostic> AnalyzeOrphanItemClass(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return EquatableArray<PendingDiagnostic>.Empty;
        }

        var compilation = ctx.SemanticModel.Compilation;
        var bindAttrDef = compilation.GetTypeByMetadataName(GeneratorConstants.BindEntityRegistryAttributeMetadataName);
        if (bindAttrDef is not null &&
            classSymbol.GetAttributes().Any(a =>
                a.AttributeClass is { IsGenericType: true } ac &&
                SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, bindAttrDef)))
        {
            return EquatableArray<PendingDiagnostic>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<PendingDiagnostic>();
        foreach (var attr in ctx.Attributes)
        {
            if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } ac)
            {
                continue;
            }

            var entityName = ac.TypeArguments[0].Name;
            var loc = SourceLocationInfo.From(
                attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? classSymbol.Locations.FirstOrDefault());

            builder.Add(new PendingDiagnostic(
                "SPM011",
                loc,
                entityName,
                classSymbol.Name,
                "requires at least one [BindEntityRegistry<TRegistry>] attribute on this class"));
        }

        return new EquatableArray<PendingDiagnostic>(builder.ToImmutable());
    }

    public static DbContextBindingModel? AnalyzeBoundDbContext(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol dbContextSymbol)
        {
            return null;
        }

        var compilation = ctx.SemanticModel.Compilation;
        var dbContextBase = compilation.GetTypeByMetadataName(GeneratorConstants.EfCoreDbContextMetadataName);
        var bindAttrDef = compilation.GetTypeByMetadataName(GeneratorConstants.BindEntityRegistryAttributeMetadataName);
        if (dbContextBase is null || bindAttrDef is null)
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<PendingDiagnostic>();
        var typeLocation = SourceLocationInfo.From(dbContextSymbol.Locations.FirstOrDefault());

        if (!ValidateDbContextDeclaration(dbContextSymbol, dbContextBase, typeLocation, diagnostics))
        {
            return CreateDiagnosticsOnlyModel(dbContextSymbol, diagnostics.ToImmutable());
        }

        var boundRegistries = ParseBoundRegistries(dbContextSymbol, bindAttrDef, typeLocation, diagnostics);
        var itemAttrDef = compilation.GetTypeByMetadataName(GeneratorConstants.EntityRegistryItemAttributeMetadataName);
        var itemOverrides = ParseItemOverrides(dbContextSymbol, itemAttrDef, typeLocation, diagnostics);
        var existingMembers = InspectExistingMembers(compilation, dbContextSymbol);

        var (properties, boundEntityTypes) = ResolveBoundProperties(
            compilation,
            dbContextSymbol,
            boundRegistries,
            itemOverrides,
            existingMembers,
            typeLocation,
            diagnostics);

        var wiring = AnalyzeWiring(compilation, dbContextSymbol, dbContextBase, typeLocation, diagnostics);

        return new DbContextBindingModel(
            HintName: BuildHintName(dbContextSymbol),
            SimpleName: dbContextSymbol.Name,
            Namespace: dbContextSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : dbContextSymbol.ContainingNamespace.ToDisplayString(),
            ContainingTypeDeclarations: new EquatableArray<string>(GetContainingTypeDeclarations(dbContextSymbol)),
            Properties: new EquatableArray<GeneratedDbContextProperty>(properties),
            BoundEntityTypeNames: new EquatableArray<string>(boundEntityTypes),
            GenerateConstructor: wiring.GenerateConstructor,
            GenerateOnModelCreating: wiring.GenerateOnModelCreating,
            ExistingSchemaMemberName: wiring.ExistingSchemaMemberName,
            Diagnostics: new EquatableArray<PendingDiagnostic>(diagnostics.ToImmutable()));
    }

    private static bool ValidateDbContextDeclaration(
        INamedTypeSymbol dbContextSymbol,
        INamedTypeSymbol dbContextBase,
        SourceLocationInfo? typeLocation,
        ImmutableArray<PendingDiagnostic>.Builder diagnostics)
    {
        var isPartial = RoslynSymbolHelper.IsDeclaredPartial(dbContextSymbol);
        var inheritsDbContext = RoslynSymbolHelper.InheritsFrom(dbContextSymbol, dbContextBase);
        if (dbContextSymbol.IsSealed && isPartial && inheritsDbContext && !dbContextSymbol.IsAbstract)
        {
            return true;
        }

        diagnostics.Add(new PendingDiagnostic("SPM008", typeLocation, dbContextSymbol.ToDisplayString()));
        return false;
    }

    private static List<BoundRegistryInput> ParseBoundRegistries(
        INamedTypeSymbol dbContextSymbol,
        INamedTypeSymbol bindAttrDef,
        SourceLocationInfo? typeLocation,
        ImmutableArray<PendingDiagnostic>.Builder diagnostics)
    {
        var boundRegistries = new List<BoundRegistryInput>();
        var seenRegistryNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attr in dbContextSymbol.GetAttributes())
        {
            if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } ac ||
                !SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, bindAttrDef) ||
                ac.TypeArguments[0] is not INamedTypeSymbol regSymbol)
            {
                continue;
            }

            var attrLoc = SourceLocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()) ?? typeLocation;
            var regFullName = regSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!seenRegistryNames.Add(regFullName))
            {
                diagnostics.Add(new PendingDiagnostic("SPM010", attrLoc, "[BindEntityRegistry]", regSymbol.Name, dbContextSymbol.Name));
                continue;
            }

            var readOnly = RoslynSymbolHelper.GetNamedBool(attr, "ReadOnly", out _);
            var pluralizeVal = RoslynSymbolHelper.GetNamedBool(attr, "Pluralize", out var hasPluralize);
            var pluralize = !hasPluralize || pluralizeVal;
            boundRegistries.Add(new BoundRegistryInput(regSymbol, readOnly, pluralize, attrLoc));
        }

        return boundRegistries;
    }

    private static Dictionary<string, ItemOverrideInput> ParseItemOverrides(
        INamedTypeSymbol dbContextSymbol,
        INamedTypeSymbol? itemAttrDef,
        SourceLocationInfo? typeLocation,
        ImmutableArray<PendingDiagnostic>.Builder diagnostics)
    {
        var itemOverrides = new Dictionary<string, ItemOverrideInput>(StringComparer.Ordinal);
        if (itemAttrDef is null)
        {
            return itemOverrides;
        }

        foreach (var attr in dbContextSymbol.GetAttributes())
        {
            if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } ac ||
                !SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, itemAttrDef) ||
                ac.TypeArguments[0] is not INamedTypeSymbol entitySymbol)
            {
                continue;
            }

            var attrLoc = SourceLocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()) ?? typeLocation;
            var entityFullName = entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var readOnlyVal = RoslynSymbolHelper.GetNamedBool(attr, "ReadOnly", out var hasReadOnly);
            var pluralizeVal = RoslynSymbolHelper.GetNamedBool(attr, "Pluralize", out var hasPluralize);
            var excludeVal = RoslynSymbolHelper.GetNamedBool(attr, "Exclude", out _);

            if (excludeVal && (hasReadOnly || hasPluralize))
            {
                diagnostics.Add(new PendingDiagnostic("SPM009", attrLoc, entitySymbol.Name, dbContextSymbol.Name));
                continue;
            }

            if (itemOverrides.ContainsKey(entityFullName))
            {
                diagnostics.Add(new PendingDiagnostic("SPM010", attrLoc, "[EntityRegistryItem]", entitySymbol.Name, dbContextSymbol.Name));
                continue;
            }

            itemOverrides[entityFullName] = new ItemOverrideInput(
                entitySymbol, entityFullName, hasReadOnly, readOnlyVal, hasPluralize, pluralizeVal, excludeVal, attrLoc);
        }

        return itemOverrides;
    }

    private static ExistingMembersInfo InspectExistingMembers(Compilation compilation, INamedTypeSymbol dbContextSymbol)
    {
        var dbSetDef = compilation.GetTypeByMetadataName(GeneratorConstants.EfCoreDbSetMetadataName);
        var queryableDef = compilation.GetTypeByMetadataName(GeneratorConstants.SystemQueryableMetadataName);
        var existingEntityProps = new HashSet<string>(StringComparer.Ordinal);
        var existingMemberNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in dbContextSymbol.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            existingMemberNames.Add(member.Name);

            if (member is IPropertySymbol prop &&
                prop.Type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } propType &&
                ((dbSetDef is not null && SymbolEqualityComparer.Default.Equals(propType.OriginalDefinition, dbSetDef)) ||
                 (queryableDef is not null && SymbolEqualityComparer.Default.Equals(propType.OriginalDefinition, queryableDef))))
            {
                existingEntityProps.Add(propType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        return new ExistingMembersInfo(existingEntityProps, existingMemberNames);
    }

    private static (ImmutableArray<GeneratedDbContextProperty> Properties, ImmutableArray<string> BoundEntityTypes)
        ResolveBoundProperties(
            Compilation compilation,
            INamedTypeSymbol dbContextSymbol,
            List<BoundRegistryInput> boundRegistries,
            Dictionary<string, ItemOverrideInput> itemOverrides,
            ExistingMembersInfo existingMembers,
            SourceLocationInfo? typeLocation,
            ImmutableArray<PendingDiagnostic>.Builder diagnostics)
    {
        var entityMarkerBase = compilation.GetTypeByMetadataName(GeneratorConstants.EntityMarkerAttributeMetadataName);
        var matchedOverrideKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenEntities = new HashSet<string>(StringComparer.Ordinal);
        var usedPropertyNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var generatedProps = ImmutableArray.CreateBuilder<GeneratedDbContextProperty>();
        var boundEntityTypeNames = ImmutableArray.CreateBuilder<string>();

        foreach (var reg in boundRegistries)
        {
            var entities = ResolveRegistryEntities(compilation, reg.RegistrySymbol, entityMarkerBase);
            foreach (var entity in entities)
            {
                if (!seenEntities.Add(entity.FullTypeName))
                {
                    continue;
                }

                var hasOverride = itemOverrides.TryGetValue(entity.FullTypeName, out var itemOverride);
                if (hasOverride)
                {
                    matchedOverrideKeys.Add(entity.FullTypeName);
                }

                if (hasOverride && itemOverride!.Exclude)
                {
                    continue;
                }

                boundEntityTypeNames.Add(entity.FullTypeName);

                if (existingMembers.EntityPropertyTypes.Contains(entity.FullTypeName))
                {
                    continue;
                }

                var isReadOnly = hasOverride && itemOverride!.HasReadOnly
                    ? itemOverride.ReadOnly
                    : reg.ReadOnly || entity.MarkedReadOnly;

                var shouldPluralize = hasOverride && itemOverride!.HasPluralize
                    ? itemOverride.Pluralize
                    : reg.Pluralize;

                var propName = shouldPluralize
                    ? EnglishPluralizer.PluralizeIdentifier(entity.SimpleName)
                    : entity.SimpleName;

                if (existingMembers.MemberNames.Contains(propName))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        "SPM012", typeLocation, propName, entity.SimpleName, dbContextSymbol.Name, $"an existing member '{propName}'"));
                    continue;
                }

                if (usedPropertyNames.TryGetValue(propName, out var otherEntity))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        "SPM012", typeLocation, propName, entity.SimpleName, dbContextSymbol.Name, $"another bound entity '{otherEntity}'"));
                    continue;
                }

                usedPropertyNames[propName] = entity.SimpleName;
                generatedProps.Add(new GeneratedDbContextProperty(entity.FullTypeName, propName, isReadOnly));
            }
        }

        foreach (var kv in itemOverrides)
        {
            if (!matchedOverrideKeys.Contains(kv.Key))
            {
                diagnostics.Add(new PendingDiagnostic(
                    "SPM011",
                    kv.Value.Location,
                    kv.Value.EntitySymbol.Name,
                    dbContextSymbol.Name,
                    "targets an entity not found in any bound [BindEntityRegistry<TRegistry>] on this class"));
            }
        }

        var sortedProps = generatedProps.OrderBy(static p => p.PropertyName, StringComparer.Ordinal).ToImmutableArray();
        var sortedBoundTypes = boundEntityTypeNames.OrderBy(static t => t, StringComparer.Ordinal).ToImmutableArray();
        return (sortedProps, sortedBoundTypes);
    }

    private static WiringResult AnalyzeWiring(
        Compilation compilation,
        INamedTypeSymbol dbContextSymbol,
        INamedTypeSymbol dbContextBase,
        SourceLocationInfo? typeLocation,
        ImmutableArray<PendingDiagnostic>.Builder diagnostics)
    {
        var databaseModelSymbol = compilation.GetTypeByMetadataName(GeneratorConstants.DatabaseModelMetadataName);
        if (databaseModelSymbol is null)
        {
            return new WiringResult(false, false, null);
        }

        var modelBuilderSymbol = compilation.GetTypeByMetadataName(GeneratorConstants.EfCoreModelBuilderMetadataName);
        var hasExplicitConstructor = dbContextSymbol.InstanceConstructors.Any(static c => !c.IsImplicitlyDeclared);
        var existingOnModelCreating = FindCustomOnModelCreating(dbContextSymbol, dbContextBase, modelBuilderSymbol);
        var existingSchemaMember = FindAccessibleDatabaseModelMember(dbContextSymbol, databaseModelSymbol);

        if (!hasExplicitConstructor && existingOnModelCreating is null)
        {
            return new WiringResult(true, true, null);
        }

        if (hasExplicitConstructor && existingOnModelCreating is null)
        {
            if (existingSchemaMember is not null)
            {
                return new WiringResult(false, true, existingSchemaMember);
            }

            diagnostics.Add(new PendingDiagnostic(
                "SPM013",
                typeLocation,
                dbContextSymbol.Name,
                "declares a custom constructor without a DatabaseModel field or property, so OnModelCreating was not auto-generated; override OnModelCreating and call modelBuilder.ApplySchemaToEfCore(schema)"));
            return new WiringResult(false, false, null);
        }

        if (existingOnModelCreating is not null && !CallsApplySchemaToEfCore(existingOnModelCreating))
        {
            var methodLoc = SourceLocationInfo.From(existingOnModelCreating.Locations.FirstOrDefault()) ?? typeLocation;
            diagnostics.Add(new PendingDiagnostic(
                "SPM013",
                methodLoc,
                dbContextSymbol.Name,
                "overrides OnModelCreating without calling ApplySchemaToEfCore(...); bound entity schemas may not be applied to EF Core"));
        }

        return new WiringResult(false, false, existingSchemaMember);
    }

    private static IMethodSymbol? FindCustomOnModelCreating(
        INamedTypeSymbol dbContextSymbol,
        INamedTypeSymbol dbContextBase,
        INamedTypeSymbol? modelBuilderSymbol)
    {
        if (modelBuilderSymbol is null)
        {
            return null;
        }

        // Walk derived class and any custom BaseDbContext<T> up to (but excluding) EF Core's DbContext
        var current = dbContextSymbol;
        while (current is not null && !SymbolEqualityComparer.Default.Equals(current, dbContextBase))
        {
            foreach (var member in current.GetMembers("OnModelCreating"))
            {
                if (member is IMethodSymbol method &&
                    !method.IsImplicitlyDeclared &&
                    method.Parameters.Length == 1 &&
                    SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, modelBuilderSymbol))
                {
                    return method;
                }
            }

            current = current.BaseType;
        }

        return null;
    }

    private static bool CallsApplySchemaToEfCore(IMethodSymbol onModelCreatingMethod)
    {
        // If inherited from a metadata-only referenced assembly (no syntax refs), assume base class handles wiring
        if (onModelCreatingMethod.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var syntaxRef in onModelCreatingMethod.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax();
            foreach (var id in syntax.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (string.Equals(id.Identifier.ValueText, "ApplySchemaToEfCore", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string? FindAccessibleDatabaseModelMember(
        INamedTypeSymbol dbContextSymbol,
        INamedTypeSymbol databaseModelSymbol)
    {
        var current = dbContextSymbol;
        var isDerived = false;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member.IsImplicitlyDeclared || member.IsStatic)
                {
                    continue;
                }

                if (isDerived && member.DeclaredAccessibility == Accessibility.Private)
                {
                    continue;
                }

                if (member is IFieldSymbol field &&
                    SymbolEqualityComparer.Default.Equals(field.Type, databaseModelSymbol))
                {
                    return field.Name;
                }

                if (member is IPropertySymbol prop &&
                    prop.GetMethod is not null &&
                    SymbolEqualityComparer.Default.Equals(prop.Type, databaseModelSymbol))
                {
                    return prop.Name;
                }
            }

            current = current.BaseType;
            isDerived = true;
        }

        return null;
    }

    private static List<DiscoveredEntity> ResolveRegistryEntities(
        Compilation compilation,
        INamedTypeSymbol registrySymbol,
        INamedTypeSymbol? entityMarkerBase)
    {
        var markerSymbol = ResolveRegistryMarker(compilation, registrySymbol);
        if (markerSymbol is null)
        {
            return [];
        }

        var results = new Dictionary<string, DiscoveredEntity>(StringComparer.Ordinal);
        CollectCrossAssemblyManifestEntities(compilation, markerSymbol, entityMarkerBase, results);
        CollectLocalEntities(compilation.Assembly.GlobalNamespace, markerSymbol, entityMarkerBase, results);

        return results.Values
            .OrderBy(static e => e.FullTypeName, StringComparer.Ordinal)
            .ToList();
    }

    private static void CollectCrossAssemblyManifestEntities(
        Compilation compilation,
        INamedTypeSymbol markerSymbol,
        INamedTypeSymbol? entityMarkerBase,
        Dictionary<string, DiscoveredEntity> results)
    {
        var manifestAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestAttributeMetadataName);
        var manifestEntryAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestEntryAttributeMetadataName);
        if (manifestAttr is null || manifestEntryAttr is null)
        {
            return;
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
            {
                continue;
            }

            var genNs = RoslynSymbolHelper.FindNamespace(asm.GlobalNamespace, GeneratorConstants.GeneratedNamespace);
            if (genNs is null)
            {
                continue;
            }

            foreach (var type in genNs.GetTypeMembers())
            {
                if (!type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, manifestAttr)))
                {
                    continue;
                }

                foreach (var entryAttr in type.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(entryAttr.AttributeClass, manifestEntryAttr) ||
                        entryAttr.ConstructorArguments.Length < 2 ||
                        entryAttr.ConstructorArguments[0].Value is not INamedTypeSymbol entryMarker ||
                        !SymbolEqualityComparer.Default.Equals(entryMarker, markerSymbol))
                    {
                        continue;
                    }

                    var entityArgs = entryAttr.ConstructorArguments[1];
                    if (entityArgs.Kind != TypedConstantKind.Array)
                    {
                        continue;
                    }

                    foreach (var tc in entityArgs.Values)
                    {
                        if (tc.Value is INamedTypeSymbol entitySym)
                        {
                            AddDiscoveredEntity(results, entitySym, entityMarkerBase);
                        }
                    }
                }
            }
        }
    }

    private static void CollectLocalEntities(
        INamespaceSymbol ns,
        INamedTypeSymbol markerSymbol,
        INamedTypeSymbol? entityMarkerBase,
        Dictionary<string, DiscoveredEntity> results)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            InspectTypeForMarker(type, markerSymbol, entityMarkerBase, results);
        }

        foreach (var childNs in ns.GetNamespaceMembers())
        {
            CollectLocalEntities(childNs, markerSymbol, entityMarkerBase, results);
        }
    }

    private static void InspectTypeForMarker(
        INamedTypeSymbol type,
        INamedTypeSymbol markerSymbol,
        INamedTypeSymbol? entityMarkerBase,
        Dictionary<string, DiscoveredEntity> results)
    {
        if (!type.IsAbstract &&
            type.TypeKind == TypeKind.Class &&
            type.TypeParameters.Length == 0 &&
            type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, markerSymbol)))
        {
            AddDiscoveredEntity(results, type, entityMarkerBase);
        }

        foreach (var nested in type.GetTypeMembers())
        {
            InspectTypeForMarker(nested, markerSymbol, entityMarkerBase, results);
        }
    }

    private static void AddDiscoveredEntity(
        Dictionary<string, DiscoveredEntity> results,
        INamedTypeSymbol entitySym,
        INamedTypeSymbol? entityMarkerBase)
    {
        var fullName = entitySym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (results.ContainsKey(fullName))
        {
            return;
        }

        var markedReadOnly = RoslynSymbolHelper.IsMarkedReadOnly(entitySym, entityMarkerBase);
        results[fullName] = new DiscoveredEntity(entitySym, fullName, entitySym.Name, markedReadOnly);
    }

    private static INamedTypeSymbol? ResolveRegistryMarker(Compilation compilation, INamedTypeSymbol registrySymbol)
    {
        var defaultRegAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityRegistryAttributeMetadataName);
        var genericRegAttr = compilation.GetTypeByMetadataName(GeneratorConstants.GenericEntityRegistryAttributeMetadataName);

        foreach (var attr in registrySymbol.GetAttributes())
        {
            var ac = attr.AttributeClass;
            if (ac is null)
            {
                continue;
            }

            if (genericRegAttr is not null &&
                ac.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, genericRegAttr) &&
                ac.TypeArguments.Length == 1 &&
                ac.TypeArguments[0] is INamedTypeSymbol customMarker)
            {
                return customMarker;
            }

            if (defaultRegAttr is not null && SymbolEqualityComparer.Default.Equals(ac, defaultRegAttr))
            {
                return compilation.GetTypeByMetadataName(GeneratorConstants.DefaultEntityAttributeMetadataName);
            }
        }

        return null;
    }

    private static ImmutableArray<string> GetContainingTypeDeclarations(INamedTypeSymbol symbol)
    {
        var stack = new Stack<string>();
        var current = symbol.ContainingType;
        while (current is not null)
        {
            var kind = current.TypeKind switch
            {
                TypeKind.Struct => "partial struct",
                TypeKind.Interface => "partial interface",
                _ => "partial class"
            };
            stack.Push($"{kind} {current.Name}");
            current = current.ContainingType;
        }

        return [.. stack];
    }

    private static string BuildHintName(INamedTypeSymbol symbol)
    {
        var fullName = GeneratorConstants.StripGlobalPrefix(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return $"{GeneratorConstants.SanitizeAssemblyName(fullName)}.DbContextBinding.g.cs";
    }

    private static DbContextBindingModel CreateDiagnosticsOnlyModel(
        INamedTypeSymbol symbol,
        ImmutableArray<PendingDiagnostic> diagnostics) =>
        new(
            HintName: BuildHintName(symbol),
            SimpleName: symbol.Name,
            Namespace: null,
            ContainingTypeDeclarations: EquatableArray<string>.Empty,
            Properties: EquatableArray<GeneratedDbContextProperty>.Empty,
            BoundEntityTypeNames: EquatableArray<string>.Empty,
            GenerateConstructor: false,
            GenerateOnModelCreating: false,
            ExistingSchemaMemberName: null,
            Diagnostics: new EquatableArray<PendingDiagnostic>(diagnostics));
}
