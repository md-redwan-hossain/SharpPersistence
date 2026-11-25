using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpPersistence.Migrator.Generators;

[Generator]
public sealed class DbContextBindingGenerator : IIncrementalGenerator
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

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var boundDbContexts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.BindEntityRegistryAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeBoundDbContext(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        var orphanItemDiagnostics = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.EntityRegistryItemAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeOrphanItemClass(ctx))
            .Where(static arr => arr.Length > 0);

        context.RegisterSourceOutput(boundDbContexts, static (spc, model) =>
        {
            foreach (var diag in model.Diagnostics)
            {
                ReportDiagnostic(spc, diag);
            }

            if (model.Properties.Length == 0 && !model.GenerateConstructor && !model.GenerateOnModelCreating)
            {
                return;
            }

            spc.AddSource(model.HintName, DbContextBindingEmitter.Emit(model));
        });

        context.RegisterSourceOutput(orphanItemDiagnostics, static (spc, diagnostics) =>
        {
            foreach (var diag in diagnostics)
            {
                ReportDiagnostic(spc, diag);
            }
        });
    }

    private static EquatableArray<PendingDiagnostic> AnalyzeOrphanItemClass(GeneratorAttributeSyntaxContext ctx)
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
            var loc = SourceLocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? classSymbol.Locations.FirstOrDefault());
            builder.Add(new PendingDiagnostic(
                "SPM011",
                loc,
                entityName,
                classSymbol.Name,
                "requires at least one [BindEntityRegistry<TRegistry>] attribute on this class"));
        }

        return new EquatableArray<PendingDiagnostic>(builder.ToImmutable());
    }

    private static DbContextBindingModel? AnalyzeBoundDbContext(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol dbContextSymbol)
        {
            return null;
        }

        var compilation = ctx.SemanticModel.Compilation;
        var dbContextBase = compilation.GetTypeByMetadataName(GeneratorConstants.EfCoreDbContextMetadataName);
        var bindAttrDef = compilation.GetTypeByMetadataName(GeneratorConstants.BindEntityRegistryAttributeMetadataName);
        var itemAttrDef = compilation.GetTypeByMetadataName(GeneratorConstants.EntityRegistryItemAttributeMetadataName);
        if (dbContextBase is null || bindAttrDef is null)
        {
            return null;
        }

        var diagnostics = ImmutableArray.CreateBuilder<PendingDiagnostic>();
        var typeLocation = SourceLocationInfo.From(dbContextSymbol.Locations.FirstOrDefault());
        var displayName = dbContextSymbol.ToDisplayString();

        var isPartial = IsDeclaredPartial(dbContextSymbol);
        var inheritsDbContext = InheritsFrom(dbContextSymbol, dbContextBase);
        if (!dbContextSymbol.IsSealed || !isPartial || !inheritsDbContext || dbContextSymbol.IsAbstract)
        {
            diagnostics.Add(new PendingDiagnostic("SPM008", typeLocation, displayName));
            return CreateDiagnosticsOnlyModel(dbContextSymbol, diagnostics.ToImmutable());
        }

        // 1. Parse [BindEntityRegistry<TRegistry>] attributes
        var boundRegistries = new List<BoundRegistryInput>();
        var seenRegistryNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in dbContextSymbol.GetAttributes())
        {
            if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } ac ||
                !SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, bindAttrDef))
            {
                continue;
            }

            if (ac.TypeArguments[0] is not INamedTypeSymbol regSymbol)
            {
                continue;
            }

            var attrLoc = SourceLocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()) ?? typeLocation;
            var regFullName = regSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!seenRegistryNames.Add(regFullName))
            {
                diagnostics.Add(new PendingDiagnostic(
                    "SPM010",
                    attrLoc,
                    "[BindEntityRegistry]",
                    regSymbol.Name,
                    dbContextSymbol.Name));
                continue;
            }

            var readOnly = GetNamedBool(attr, "ReadOnly", out _);
            var pluralizeVal = GetNamedBool(attr, "Pluralize", out var hasPluralize);
            var pluralize = !hasPluralize || pluralizeVal;
            boundRegistries.Add(new BoundRegistryInput(regSymbol, readOnly, pluralize, attrLoc));
        }

        // 2. Parse [EntityRegistryItem<TEntity>] attributes
        var itemOverrides = new Dictionary<string, ItemOverrideInput>(StringComparer.Ordinal);
        if (itemAttrDef is not null)
        {
            foreach (var attr in dbContextSymbol.GetAttributes())
            {
                if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } ac ||
                    !SymbolEqualityComparer.Default.Equals(ac.OriginalDefinition, itemAttrDef))
                {
                    continue;
                }

                if (ac.TypeArguments[0] is not INamedTypeSymbol entitySymbol)
                {
                    continue;
                }

                var attrLoc = SourceLocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()) ?? typeLocation;
                var entityFullName = entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                var readOnlyVal = GetNamedBool(attr, "ReadOnly", out var hasReadOnly);
                var pluralizeVal = GetNamedBool(attr, "Pluralize", out var hasPluralize);
                var excludeVal = GetNamedBool(attr, "Exclude", out _);

                if (excludeVal && (hasReadOnly || hasPluralize))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        "SPM009",
                        attrLoc,
                        entitySymbol.Name,
                        dbContextSymbol.Name));
                    continue;
                }

                if (itemOverrides.ContainsKey(entityFullName))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        "SPM010",
                        attrLoc,
                        "[EntityRegistryItem]",
                        entitySymbol.Name,
                        dbContextSymbol.Name));
                    continue;
                }

                itemOverrides[entityFullName] = new ItemOverrideInput(
                    entitySymbol,
                    entityFullName,
                    hasReadOnly,
                    readOnlyVal,
                    hasPluralize,
                    pluralizeVal,
                    excludeVal,
                    attrLoc);
            }
        }

        // 3. Collect existing members on the DbContext (manual DbSet<T>/IQueryable<T> skips + name collision checks)
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

        // 4. Resolve entities across all bound registries
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

                // Skip generating a duplicate property if the user already declared DbSet<T> or IQueryable<T> manually
                if (existingEntityProps.Contains(entity.FullTypeName))
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

                if (existingMemberNames.Contains(propName))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        "SPM012",
                        typeLocation,
                        propName,
                        entity.SimpleName,
                        dbContextSymbol.Name,
                        $"an existing member '{propName}'"));
                    continue;
                }

                if (usedPropertyNames.TryGetValue(propName, out var otherEntity))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        "SPM012",
                        typeLocation,
                        propName,
                        entity.SimpleName,
                        dbContextSymbol.Name,
                        $"another bound entity '{otherEntity}'"));
                    continue;
                }

                usedPropertyNames[propName] = entity.SimpleName;
                generatedProps.Add(new GeneratedDbContextProperty(entity.FullTypeName, propName, isReadOnly));
            }
        }

        // Report SPM011 for any [EntityRegistryItem<T>] not found in any bound registry
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

        // 5. Constructor & OnModelCreating detection + SPM013 warnings
        var databaseModelSymbol = compilation.GetTypeByMetadataName(GeneratorConstants.DatabaseModelMetadataName);
        var modelBuilderSymbol = compilation.GetTypeByMetadataName(GeneratorConstants.EfCoreModelBuilderMetadataName);
        var hasExplicitConstructor = dbContextSymbol.InstanceConstructors.Any(static c => !c.IsImplicitlyDeclared);
        var existingOnModelCreating = FindDeclaredOnModelCreating(dbContextSymbol, modelBuilderSymbol);
        var existingSchemaMember = databaseModelSymbol is not null
            ? FindAccessibleDatabaseModelMember(dbContextSymbol, databaseModelSymbol)
            : null;

        var generateConstructor = false;
        var generateOnModelCreating = false;

        if (databaseModelSymbol is not null)
        {
            if (!hasExplicitConstructor && existingOnModelCreating is null)
            {
                generateConstructor = true;
                generateOnModelCreating = true;
            }
            else if (hasExplicitConstructor && existingOnModelCreating is null)
            {
                if (existingSchemaMember is not null)
                {
                    generateOnModelCreating = true;
                }
                else
                {
                    diagnostics.Add(new PendingDiagnostic(
                        "SPM013",
                        typeLocation,
                        dbContextSymbol.Name,
                        "declares a custom constructor without a DatabaseModel field or property, so OnModelCreating was not auto-generated; override OnModelCreating and call modelBuilder.ApplySchemaToEfCore(schema)"));
                }
            }
            else if (existingOnModelCreating is not null && !CallsApplySchemaToEfCore(existingOnModelCreating))
            {
                var methodLoc = SourceLocationInfo.From(existingOnModelCreating.Locations.FirstOrDefault()) ?? typeLocation;
                diagnostics.Add(new PendingDiagnostic(
                    "SPM013",
                    methodLoc,
                    dbContextSymbol.Name,
                    "overrides OnModelCreating without calling ApplySchemaToEfCore(...); bound entity schemas may not be applied to EF Core"));
            }
        }

        var sortedProps = generatedProps
            .OrderBy(static p => p.PropertyName, StringComparer.Ordinal)
            .ToImmutableArray();
        var sortedBoundTypes = boundEntityTypeNames
            .OrderBy(static t => t, StringComparer.Ordinal)
            .ToImmutableArray();

        return new DbContextBindingModel(
            HintName: BuildHintName(dbContextSymbol),
            SimpleName: dbContextSymbol.Name,
            Namespace: dbContextSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : dbContextSymbol.ContainingNamespace.ToDisplayString(),
            ContainingTypeDeclarations: new EquatableArray<string>(GetContainingTypeDeclarations(dbContextSymbol)),
            Properties: new EquatableArray<GeneratedDbContextProperty>(sortedProps),
            BoundEntityTypeNames: new EquatableArray<string>(sortedBoundTypes),
            GenerateConstructor: generateConstructor,
            GenerateOnModelCreating: generateOnModelCreating,
            ExistingSchemaMemberName: existingSchemaMember,
            Diagnostics: new EquatableArray<PendingDiagnostic>(diagnostics.ToImmutable()));
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

        // 1. Cross-assembly O(1) manifest lookup
        var manifestAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestAttributeMetadataName);
        var manifestEntryAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestEntryAttributeMetadataName);
        if (manifestAttr is not null && manifestEntryAttr is not null)
        {
            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                {
                    continue;
                }

                var genNs = FindNamespace(asm.GlobalNamespace, GeneratorConstants.GeneratedNamespace);
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
                            entryAttr.ConstructorArguments.Length < 2)
                        {
                            continue;
                        }

                        if (entryAttr.ConstructorArguments[0].Value is not INamedTypeSymbol entryMarker ||
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

        // 2. Local compilation entities marked with the same marker attribute
        CollectLocalEntities(compilation.Assembly.GlobalNamespace, markerSymbol, entityMarkerBase, results);

        return results.Values
            .OrderBy(static e => e.FullTypeName, StringComparer.Ordinal)
            .ToList();
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

        var markedReadOnly = IsEntityMarkedReadOnly(entitySym, entityMarkerBase);
        results[fullName] = new DiscoveredEntity(entitySym, fullName, entitySym.Name, markedReadOnly);
    }

    private static bool IsEntityMarkedReadOnly(INamedTypeSymbol entitySym, INamedTypeSymbol? entityMarkerBase)
    {
        if (entityMarkerBase is null)
        {
            return false;
        }

        foreach (var attr in entitySym.GetAttributes())
        {
            if (attr.AttributeClass is null || !InheritsFrom(attr.AttributeClass, entityMarkerBase))
            {
                continue;
            }

            if (GetNamedBool(attr, "ReadOnly", out _))
            {
                return true;
            }
        }

        return false;
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

    private static IMethodSymbol? FindDeclaredOnModelCreating(
        INamedTypeSymbol dbContextSymbol,
        INamedTypeSymbol? modelBuilderSymbol)
    {
        if (modelBuilderSymbol is null)
        {
            return null;
        }

        foreach (var member in dbContextSymbol.GetMembers("OnModelCreating"))
        {
            if (member is IMethodSymbol method &&
                !method.IsImplicitlyDeclared &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, modelBuilderSymbol))
            {
                return method;
            }
        }

        return null;
    }

    private static bool CallsApplySchemaToEfCore(IMethodSymbol onModelCreatingMethod)
    {
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

    private static bool GetNamedBool(AttributeData attr, string name, out bool isExplicitlySet)
    {
        foreach (var kv in attr.NamedArguments)
        {
            if (string.Equals(kv.Key, name, StringComparison.Ordinal))
            {
                isExplicitlySet = true;
                return kv.Value.Value is true;
            }
        }

        isExplicitlySet = false;
        return false;
    }

    private static bool IsDeclaredPartial(INamedTypeSymbol symbol)
    {
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is ClassDeclarationSyntax cds &&
                cds.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol targetBase)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, targetBase))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static INamespaceSymbol? FindNamespace(INamespaceSymbol root, string fullName)
    {
        var current = root;
        foreach (var part in fullName.Split('.'))
        {
            current = current.GetNamespaceMembers()
                .FirstOrDefault(n => string.Equals(n.Name, part, StringComparison.Ordinal));
            if (current is null)
            {
                return null;
            }
        }

        return current;
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

    private static void ReportDiagnostic(SourceProductionContext spc, PendingDiagnostic diag)
    {
        var location = diag.Location?.ToLocation() ?? Location.None;
        switch (diag.DiagnosticId)
        {
            case "SPM008":
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DbContextMustBeSealedPartial, location, diag.Arg0));
                break;
            case "SPM009":
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.InvalidRegistryItemCombination, location, diag.Arg0, diag.Arg1));
                break;
            case "SPM010":
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DuplicateDbContextBindingAttribute, location, diag.Arg0, diag.Arg1, diag.Arg2));
                break;
            case "SPM011":
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.OrphanEntityRegistryItem, location, diag.Arg0, diag.Arg1, diag.Arg2));
                break;
            case "SPM012":
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DbContextPropertyNameCollision, location, diag.Arg0, diag.Arg1, diag.Arg2, diag.Arg3));
                break;
            case "SPM013":
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DbContextSchemaNotWired, location, diag.Arg0, diag.Arg1));
                break;
        }
    }
}
