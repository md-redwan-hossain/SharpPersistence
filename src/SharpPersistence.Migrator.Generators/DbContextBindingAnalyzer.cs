using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.Generators;

internal static class DbContextBindingAnalyzer
{
    private readonly record struct BoundRegistryInput
    {
        public required INamedTypeSymbol RegistrySymbol { get; init; }
        public required bool ReadOnly { get; init; }
        public required bool Pluralize { get; init; }
        public required SourceLocationInfo? Location { get; init; }
    }

    private readonly record struct ItemOverrideInput
    {
        public required INamedTypeSymbol EntitySymbol { get; init; }
        public required string EntityFullName { get; init; }
        public required bool HasReadOnly { get; init; }
        public required bool ReadOnly { get; init; }
        public required bool HasPluralize { get; init; }
        public required bool Pluralize { get; init; }
        public required bool Exclude { get; init; }
        public required SourceLocationInfo? Location { get; init; }
    }

    private readonly record struct ExistingMembersInfo
    {
        public required HashSet<string> EntityPropertyTypes { get; init; }
        public required HashSet<string> MemberNames { get; init; }
    }

    private readonly record struct WiringResult
    {
        public required bool GenerateConstructor { get; init; }
        public required bool GenerateOnModelCreating { get; init; }
        public required string? ExistingSchemaMemberName { get; init; }
    }

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
                DiagnosticIds.OrphanEntityRegistryItem,
                loc,
                entityName,
                classSymbol.Name,
                "requires at least one [BindEntityRegistry<TRegistry>] attribute on this class"));
        }

        return new EquatableArray<PendingDiagnostic>(builder.ToImmutable());
    }

    public static BoundDbContextSnapshot? AnalyzeBoundDbContext(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            return CreateInvalidSnapshot(dbContextSymbol, typeLocation, diagnostics.ToImmutable());
        }

        var boundRegistries = ParseBoundRegistries(dbContextSymbol, bindAttrDef, typeLocation, diagnostics);
        var itemAttrDef = compilation.GetTypeByMetadataName(GeneratorConstants.EntityRegistryItemAttributeMetadataName);
        var itemOverrides = ParseItemOverrides(dbContextSymbol, itemAttrDef, typeLocation, diagnostics);
        var existingMembers = InspectExistingMembers(compilation, dbContextSymbol);
        var wiring = AnalyzeWiring(compilation, dbContextSymbol, dbContextBase, typeLocation, diagnostics);

        var boundGroups = ImmutableArray.CreateBuilder<BoundRegistryGroupInfo>(boundRegistries.Count);
        var targetGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reg in boundRegistries)
        {
            var groupFullName = ResolveRegistryGroupFullName(compilation, reg.RegistrySymbol);
            if (groupFullName is null)
            {
                continue;
            }

            targetGroups.Add(groupFullName);
            boundGroups.Add(new BoundRegistryGroupInfo
            {
                GroupFullName = groupFullName,
                ReadOnly = reg.ReadOnly,
                Pluralize = reg.Pluralize
            });
        }

        var defaultEntityAttr = compilation.GetTypeByMetadataName(GeneratorConstants.DefaultEntityAttributeMetadataName);
        var genericEntityAttr = compilation.GetTypeByMetadataName(GeneratorConstants.GenericEntityAttributeMetadataName);
        var crossByGroup = IndexCrossAssemblyManifestEntities(compilation, targetGroups, defaultEntityAttr, genericEntityAttr, cancellationToken);
        var crossEntities = ImmutableArray.CreateBuilder<LocalDiscoveredEntity>();
        foreach (var list in crossByGroup.Values)
        {
            crossEntities.AddRange(list);
        }

        var overrideSnapshots = ImmutableArray.CreateBuilder<ItemOverrideSnapshot>(itemOverrides.Count);
        foreach (var ov in itemOverrides.Values)
        {
            overrideSnapshots.Add(new ItemOverrideSnapshot
            {
                EntityFullName = ov.EntityFullName,
                EntitySimpleName = ov.EntitySymbol.Name,
                HasReadOnly = ov.HasReadOnly,
                ReadOnly = ov.ReadOnly,
                HasPluralize = ov.HasPluralize,
                Pluralize = ov.Pluralize,
                Exclude = ov.Exclude,
                Location = ov.Location
            });
        }

        return new BoundDbContextSnapshot
        {
            HintName = BuildHintName(dbContextSymbol),
            SimpleName = dbContextSymbol.Name,
            Namespace = dbContextSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : dbContextSymbol.ContainingNamespace.ToDisplayString(),
            TypeLocation = typeLocation,
            ContainingTypeDeclarations = new EquatableArray<string>(GetContainingTypeDeclarations(dbContextSymbol)),
            BoundGroups = new EquatableArray<BoundRegistryGroupInfo>(boundGroups.ToImmutable()),
            CrossAssemblyEntities = new EquatableArray<LocalDiscoveredEntity>(crossEntities.ToImmutable()),
            ItemOverrides = new EquatableArray<ItemOverrideSnapshot>(overrideSnapshots.ToImmutable()),
            ExistingEntityPropertyTypes = new EquatableArray<string>([.. existingMembers.EntityPropertyTypes]),
            ExistingMemberNames = new EquatableArray<string>([.. existingMembers.MemberNames]),
            GenerateConstructor = wiring.GenerateConstructor,
            GenerateOnModelCreating = wiring.GenerateOnModelCreating,
            ExistingSchemaMemberName = wiring.ExistingSchemaMemberName,
            InitialDiagnostics = new EquatableArray<PendingDiagnostic>(diagnostics.ToImmutable()),
            IsInvalidDeclaration = false
        };
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

        diagnostics.Add(new PendingDiagnostic(DiagnosticIds.DbContextMustBeSealedPartial, typeLocation, dbContextSymbol.ToDisplayString()));
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
                diagnostics.Add(new PendingDiagnostic(DiagnosticIds.DuplicateDbContextBindingAttribute, attrLoc, KnownNames.Attributes.BindEntityRegistryDisplay, regSymbol.Name, dbContextSymbol.Name));
                continue;
            }

            var readOnly = RoslynSymbolHelper.GetNamedBool(attr, KnownNames.AttributeProperties.ReadOnly, out _);
            var pluralizeVal = RoslynSymbolHelper.GetNamedBool(attr, KnownNames.AttributeProperties.Pluralize, out var hasPluralize);
            var pluralize = !hasPluralize || pluralizeVal;
            boundRegistries.Add(new BoundRegistryInput
            {
                RegistrySymbol = regSymbol,
                ReadOnly = readOnly,
                Pluralize = pluralize,
                Location = attrLoc
            });
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

            var readOnlyVal = RoslynSymbolHelper.GetNamedBool(attr, KnownNames.AttributeProperties.ReadOnly, out var hasReadOnly);
            var pluralizeVal = RoslynSymbolHelper.GetNamedBool(attr, KnownNames.AttributeProperties.Pluralize, out var hasPluralize);
            var excludeVal = RoslynSymbolHelper.GetNamedBool(attr, KnownNames.AttributeProperties.Exclude, out _);

            if (excludeVal && (hasReadOnly || hasPluralize))
            {
                diagnostics.Add(new PendingDiagnostic(DiagnosticIds.InvalidRegistryItemCombination, attrLoc, entitySymbol.Name, dbContextSymbol.Name));
                continue;
            }

            if (itemOverrides.ContainsKey(entityFullName))
            {
                diagnostics.Add(new PendingDiagnostic(DiagnosticIds.DuplicateDbContextBindingAttribute, attrLoc, KnownNames.Attributes.EntityRegistryItemDisplay, entitySymbol.Name, dbContextSymbol.Name));
                continue;
            }

            itemOverrides[entityFullName] = new ItemOverrideInput
            {
                EntitySymbol = entitySymbol,
                EntityFullName = entityFullName,
                HasReadOnly = hasReadOnly,
                ReadOnly = readOnlyVal,
                HasPluralize = hasPluralize,
                Pluralize = pluralizeVal,
                Exclude = excludeVal,
                Location = attrLoc
            };
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

        return new ExistingMembersInfo
        {
            EntityPropertyTypes = existingEntityProps,
            MemberNames = existingMemberNames
        };
    }

    public static DbContextBindingModel FinalizeModel(
        BoundDbContextSnapshot snapshot,
        EquatableArray<LocalDiscoveredEntity> localDefault,
        EquatableArray<LocalDiscoveredEntity> localGrouped)
    {
        if (snapshot.IsInvalidDeclaration)
        {
            return new DbContextBindingModel
            {
                HintName = snapshot.HintName,
                SimpleName = snapshot.SimpleName,
                Namespace = null,
                ContainingTypeDeclarations = EquatableArray<string>.Empty,
                Properties = EquatableArray<GeneratedDbContextProperty>.Empty,
                BoundEntityTypeNames = EquatableArray<string>.Empty,
                GenerateConstructor = false,
                GenerateOnModelCreating = false,
                ExistingSchemaMemberName = null,
                Diagnostics = snapshot.InitialDiagnostics
            };
        }

        var diagnostics = ImmutableArray.CreateBuilder<PendingDiagnostic>(snapshot.InitialDiagnostics.Length);
        diagnostics.AddRange(snapshot.InitialDiagnostics);

        var entitiesByGroup = new Dictionary<string, Dictionary<string, LocalDiscoveredEntity>>(StringComparer.Ordinal);
        void IndexEntities(EquatableArray<LocalDiscoveredEntity> items)
        {
            foreach (var item in items)
            {
                if (!entitiesByGroup.TryGetValue(item.GroupFullName, out var dict))
                {
                    dict = new Dictionary<string, LocalDiscoveredEntity>(StringComparer.Ordinal);
                    entitiesByGroup[item.GroupFullName] = dict;
                }

                dict[item.FullTypeName] = item;
            }
        }

        IndexEntities(snapshot.CrossAssemblyEntities);
        IndexEntities(localDefault);
        IndexEntities(localGrouped);

        var itemOverrides = new Dictionary<string, ItemOverrideSnapshot>(StringComparer.Ordinal);
        foreach (var ov in snapshot.ItemOverrides)
        {
            itemOverrides[ov.EntityFullName] = ov;
        }

        var existingEntityProps = new HashSet<string>(snapshot.ExistingEntityPropertyTypes, StringComparer.Ordinal);
        var existingMemberNames = new HashSet<string>(snapshot.ExistingMemberNames, StringComparer.Ordinal);
        var matchedOverrideKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenEntities = new HashSet<string>(StringComparer.Ordinal);
        var usedPropertyNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var generatedProps = ImmutableArray.CreateBuilder<GeneratedDbContextProperty>();
        var boundEntityTypeNames = ImmutableArray.CreateBuilder<string>();

        foreach (var reg in snapshot.BoundGroups)
        {
            if (!entitiesByGroup.TryGetValue(reg.GroupFullName, out var groupMap))
            {
                continue;
            }

            foreach (var entity in groupMap.Values.OrderBy(static e => e.FullTypeName, StringComparer.Ordinal))
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

                if (hasOverride && itemOverride.Exclude)
                {
                    continue;
                }

                boundEntityTypeNames.Add(entity.FullTypeName);

                if (existingEntityProps.Contains(entity.FullTypeName))
                {
                    continue;
                }

                var isReadOnly = hasOverride && itemOverride.HasReadOnly
                    ? itemOverride.ReadOnly
                    : reg.ReadOnly || entity.MarkedReadOnly;

                var shouldPluralize = hasOverride && itemOverride.HasPluralize
                    ? itemOverride.Pluralize
                    : reg.Pluralize;

                var propName = shouldPluralize
                    ? EnglishPluralizer.PluralizeIdentifier(entity.SimpleName)
                    : entity.SimpleName;

                if (existingMemberNames.Contains(propName))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        DiagnosticIds.DbContextPropertyNameCollision, snapshot.TypeLocation, propName, entity.SimpleName, snapshot.SimpleName, $"an existing member '{propName}'"));
                    continue;
                }

                if (usedPropertyNames.TryGetValue(propName, out var otherEntity))
                {
                    diagnostics.Add(new PendingDiagnostic(
                        DiagnosticIds.DbContextPropertyNameCollision, snapshot.TypeLocation, propName, entity.SimpleName, snapshot.SimpleName, $"another bound entity '{otherEntity}'"));
                    continue;
                }

                usedPropertyNames[propName] = entity.SimpleName;
                generatedProps.Add(new GeneratedDbContextProperty
                {
                    EntityFullTypeName = entity.FullTypeName,
                    PropertyName = propName,
                    IsReadOnly = isReadOnly
                });
            }
        }

        foreach (var kv in itemOverrides)
        {
            if (!matchedOverrideKeys.Contains(kv.Key))
            {
                diagnostics.Add(new PendingDiagnostic(
                    DiagnosticIds.OrphanEntityRegistryItem,
                    kv.Value.Location,
                    kv.Value.EntitySimpleName,
                    snapshot.SimpleName,
                    "targets an entity not found in any bound [BindEntityRegistry<TRegistry>] on this class"));
            }
        }

        return new DbContextBindingModel
        {
            HintName = snapshot.HintName,
            SimpleName = snapshot.SimpleName,
            Namespace = snapshot.Namespace,
            ContainingTypeDeclarations = snapshot.ContainingTypeDeclarations,
            Properties = new EquatableArray<GeneratedDbContextProperty>(
                generatedProps.OrderBy(static p => p.PropertyName, StringComparer.Ordinal).ToImmutableArray()),
            BoundEntityTypeNames = new EquatableArray<string>(
                boundEntityTypeNames.OrderBy(static t => t, StringComparer.Ordinal).ToImmutableArray()),
            GenerateConstructor = snapshot.GenerateConstructor,
            GenerateOnModelCreating = snapshot.GenerateOnModelCreating,
            ExistingSchemaMemberName = snapshot.ExistingSchemaMemberName,
            Diagnostics = new EquatableArray<PendingDiagnostic>(diagnostics.ToImmutable())
        };
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
            return new WiringResult
            {
                GenerateConstructor = false,
                GenerateOnModelCreating = false,
                ExistingSchemaMemberName = null
            };
        }

        var modelBuilderSymbol = compilation.GetTypeByMetadataName(GeneratorConstants.EfCoreModelBuilderMetadataName);
        var hasExplicitConstructor = dbContextSymbol.InstanceConstructors.Any(static c => !c.IsImplicitlyDeclared);
        var existingOnModelCreating = FindCustomOnModelCreating(dbContextSymbol, dbContextBase, modelBuilderSymbol);
        var existingSchemaMember = FindAccessibleDatabaseModelMember(dbContextSymbol, databaseModelSymbol);

        if (!hasExplicitConstructor && existingOnModelCreating is null)
        {
            return new WiringResult
            {
                GenerateConstructor = true,
                GenerateOnModelCreating = true,
                ExistingSchemaMemberName = null
            };
        }

        if (hasExplicitConstructor && existingOnModelCreating is null)
        {
            if (existingSchemaMember is not null)
            {
                return new WiringResult
                {
                    GenerateConstructor = false,
                    GenerateOnModelCreating = true,
                    ExistingSchemaMemberName = existingSchemaMember
                };
            }

            diagnostics.Add(new PendingDiagnostic(
                DiagnosticIds.DbContextMissingSchemaWiring,
                typeLocation,
                dbContextSymbol.Name,
                "declares a custom constructor without a DatabaseModel field or property, so OnModelCreating was not auto-generated; override OnModelCreating and call modelBuilder.ApplySchemaToEfCore(schema)"));
            return new WiringResult
            {
                GenerateConstructor = false,
                GenerateOnModelCreating = false,
                ExistingSchemaMemberName = null
            };
        }

        if (existingOnModelCreating is not null && !CallsApplySchemaToEfCore(existingOnModelCreating))
        {
            var methodLoc = SourceLocationInfo.From(existingOnModelCreating.Locations.FirstOrDefault()) ?? typeLocation;
            diagnostics.Add(new PendingDiagnostic(
                DiagnosticIds.DbContextMissingSchemaWiring,
                methodLoc,
                dbContextSymbol.Name,
                "overrides OnModelCreating without calling ApplySchemaToEfCore(...); bound entity schemas may not be applied to EF Core"));
        }

        return new WiringResult
        {
            GenerateConstructor = false,
            GenerateOnModelCreating = false,
            ExistingSchemaMemberName = existingSchemaMember
        };
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
            foreach (var member in current.GetMembers(KnownNames.Methods.OnModelCreating))
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
                if (string.Equals(id.Identifier.ValueText, KnownNames.Methods.ApplySchemaToEfCore, StringComparison.Ordinal))
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

    internal static LocalDiscoveredEntity? ExtractLocalDefaultEntity(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol || symbol.IsAbstract || symbol.TypeParameters.Length > 0)
        {
            return null;
        }

        var markedReadOnly = ctx.Attributes.Any(static a =>
            RoslynSymbolHelper.GetNamedBool(a, KnownNames.AttributeProperties.ReadOnly, out _));

        return new LocalDiscoveredEntity
        {
            GroupFullName = "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName,
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SimpleName = symbol.Name,
            MarkedReadOnly = markedReadOnly
        };
    }

    internal static EquatableArray<LocalDiscoveredEntity> ExtractLocalGroupedEntities(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol || symbol.IsAbstract || symbol.TypeParameters.Length > 0)
        {
            return EquatableArray<LocalDiscoveredEntity>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<LocalDiscoveredEntity>(ctx.Attributes.Length);
        var fullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var attr in ctx.Attributes)
        {
            if (attr.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } ac ||
                ac.TypeArguments[0] is not INamedTypeSymbol groupSym)
            {
                continue;
            }

            builder.Add(new LocalDiscoveredEntity
            {
                GroupFullName = groupSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                FullTypeName = fullName,
                SimpleName = symbol.Name,
                MarkedReadOnly = RoslynSymbolHelper.GetNamedBool(attr, KnownNames.AttributeProperties.ReadOnly, out _)
            });
        }

        return new EquatableArray<LocalDiscoveredEntity>(builder.ToImmutable());
    }

    private static Dictionary<string, List<LocalDiscoveredEntity>> IndexCrossAssemblyManifestEntities(
        Compilation compilation,
        HashSet<string> targetGroups,
        INamedTypeSymbol? defaultEntityAttr,
        INamedTypeSymbol? genericEntityAttr,
        CancellationToken cancellationToken)
    {
        var byGroup = new Dictionary<string, List<LocalDiscoveredEntity>>(StringComparer.Ordinal);
        if (targetGroups.Count == 0)
        {
            return byGroup;
        }

        var manifestEntryAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestEntryAttributeMetadataName);
        if (manifestEntryAttr is null)
        {
            return byGroup;
        }

        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestMetadataName =
                $"{GeneratorConstants.GeneratedNamespace}.{GeneratorConstants.ManifestPrefix}{GeneratorConstants.SanitizeAssemblyName(refAsm.Name)}";
            var manifestType = refAsm.GetTypeByMetadataName(manifestMetadataName);
            if (manifestType is null)
            {
                continue;
            }

            ReadManifestTypeEntries(manifestType, manifestEntryAttr, targetGroups, defaultEntityAttr, genericEntityAttr, byGroup);
        }

        return byGroup;
    }

    private static void ReadManifestTypeEntries(
        INamedTypeSymbol manifestType,
        INamedTypeSymbol manifestEntryAttr,
        HashSet<string> targetGroups,
        INamedTypeSymbol? defaultEntityAttr,
        INamedTypeSymbol? genericEntityAttr,
        Dictionary<string, List<LocalDiscoveredEntity>> byGroup)
    {
        foreach (var entryAttr in manifestType.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(entryAttr.AttributeClass, manifestEntryAttr) ||
                entryAttr.ConstructorArguments.Length < 2 ||
                entryAttr.ConstructorArguments[0].Value is not INamedTypeSymbol groupSym)
            {
                continue;
            }

            var groupFullName = groupSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!targetGroups.Contains(groupFullName))
            {
                continue;
            }

            var entityArgs = entryAttr.ConstructorArguments[1];
            if (entityArgs.Kind != TypedConstantKind.Array)
            {
                continue;
            }

            if (!byGroup.TryGetValue(groupFullName, out var list))
            {
                list = [];
                byGroup[groupFullName] = list;
            }

            foreach (var tc in entityArgs.Values)
            {
                if (tc.Value is INamedTypeSymbol entitySym)
                {
                    list.Add(new LocalDiscoveredEntity
                    {
                        GroupFullName = groupFullName,
                        FullTypeName = entitySym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        SimpleName = entitySym.Name,
                        MarkedReadOnly = RoslynSymbolHelper.IsMarkedReadOnly(entitySym, defaultEntityAttr, genericEntityAttr)
                    });
                }
            }
        }
    }

    private static string? ResolveRegistryGroupFullName(Compilation compilation, INamedTypeSymbol registrySymbol)
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
                ac.TypeArguments[0] is INamedTypeSymbol groupSym)
            {
                return groupSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            if (defaultRegAttr is not null && SymbolEqualityComparer.Default.Equals(ac, defaultRegAttr))
            {
                return "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName;
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

    private static BoundDbContextSnapshot CreateInvalidSnapshot(
        INamedTypeSymbol symbol,
        SourceLocationInfo? typeLocation,
        ImmutableArray<PendingDiagnostic> diagnostics) =>
        new()
        {
            HintName = BuildHintName(symbol),
            SimpleName = symbol.Name,
            Namespace = null,
            TypeLocation = typeLocation,
            ContainingTypeDeclarations = EquatableArray<string>.Empty,
            BoundGroups = EquatableArray<BoundRegistryGroupInfo>.Empty,
            CrossAssemblyEntities = EquatableArray<LocalDiscoveredEntity>.Empty,
            ItemOverrides = EquatableArray<ItemOverrideSnapshot>.Empty,
            ExistingEntityPropertyTypes = EquatableArray<string>.Empty,
            ExistingMemberNames = EquatableArray<string>.Empty,
            GenerateConstructor = false,
            GenerateOnModelCreating = false,
            ExistingSchemaMemberName = null,
            InitialDiagnostics = new EquatableArray<PendingDiagnostic>(diagnostics),
            IsInvalidDeclaration = true
        };
}
