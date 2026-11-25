using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SharpPersistence.Migrator.Generators;

[Generator]
public sealed class EntityRegistryGenerator : IIncrementalGenerator
{
    private readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public static LocationInfo? From(Location? location)
        {
            if (location is null || !location.IsInSource)
            {
                return null;
            }

            var lineSpan = location.GetLineSpan();
            return new LocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
        }

        public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
    }

    private sealed record RegistryCandidate(
        string FullTypeName,
        string SimpleName,
        string? Namespace,
        ImmutableArray<string> ContainingTypeDeclarations,
        string MarkerFullName,
        bool IsSealed,
        bool IsPartial,
        bool ImplementsInterface,
        bool HasPublicConfigure,
        LocationInfo? Location);

    private sealed record ConfigCandidate(
        string ConfigFullName,
        string EntityFullName,
        bool IsValid,
        LocationInfo? Location);

    private sealed record LocalEntityEntry(string MarkerFullName, string EntityFullName);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var isDisabled = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(GeneratorConstants.DisablePropertyName, out var value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        var isRoot = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
                provider.GlobalOptions.TryGetValue(GeneratorConstants.RootPropertyName, out var value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        // O(1) discovery of non-generic [EntityRegistry]
        var defaultRegistries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.EntityRegistryAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeRegistry(ctx, isGeneric: false))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        // O(1) discovery of generic [EntityRegistry<TMarkerAttribute>]
        var genericRegistries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.GenericEntityRegistryAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeRegistry(ctx, isGeneric: true))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        // O(1) discovery of [EntityConfiguration<TEntity>]
        var configurations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.EntityConfigurationAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeConfiguration(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        // O(1) local built-in [Entity] discovery (for single-project apps)
        var localDefaultEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.DefaultEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
                        symbol.IsAbstract ||
                        symbol.TypeParameters.Length > 0)
                    {
                        return null;
                    }

                    return new LocalEntityEntry(
                        "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName,
                        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                })
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect();

        // Value-equatable local [assembly: DiscoverEntitiesBy<T>] entries (no raw SyntaxNodes stored)
        var localCustomEntities = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, _) => EntityManifestGenerator.ExtractCustomEntityEntries(ctx))
            .Where(static arr => !arr.IsDefaultOrEmpty)
            .SelectMany(static (arr, _) => arr)
            .Select(static (e, _) => new LocalEntityEntry(e.MarkerFullName, e.EntityFullName))
            .Collect();

        var registriesAndConfigs = defaultRegistries
            .Combine(genericRegistries)
            .Combine(configurations);

        var localEntities = localDefaultEntities.Combine(localCustomEntities);

        var pipeline = context.CompilationProvider
            .Combine(registriesAndConfigs)
            .Combine(localEntities)
            .Combine(isRoot)
            .Combine(isDisabled);

        context.RegisterSourceOutput(pipeline, static (spc, input) =>
        {
            if (input.Right)
            {
                return;
            }

            var compilation = input.Left.Left.Left.Left;
            if (GeneratorConstants.HasAssemblyAttribute(
                    compilation,
                    GeneratorConstants.DisableEntityDiscoveryAttributeMetadataName))
            {
                return;
            }

            var defaultRegs = input.Left.Left.Left.Right.Left.Left;
            var genericRegs = input.Left.Left.Left.Right.Left.Right;
            var configCandidates = input.Left.Left.Left.Right.Right;
            var localDefault = input.Left.Left.Right.Left;
            var localCustom = input.Left.Left.Right.Right;
            var rootMode = input.Left.Right || GeneratorConstants.HasAssemblyAttribute(
                compilation,
                GeneratorConstants.EntityRegistryRootAttributeMetadataName);

            // Skip projects that don't reference SharpPersistence.Migrator
            if (compilation.GetTypeByMetadataName(GeneratorConstants.IEntityRegistryMetadataName) is null)
            {
                return;
            }

            var allRegistries = defaultRegs.AddRange(genericRegs);

            // 1. Validate configurations & detect duplicates (SPM002, SPM003)
            var configsByEntity = new Dictionary<string, List<ConfigCandidate>>(StringComparer.Ordinal);
            foreach (var cfg in configCandidates)
            {
                if (!cfg.IsValid)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        GeneratorConstants.ConfigurationInvalid,
                        cfg.Location?.ToLocation(),
                        GeneratorConstants.StripGlobalPrefix(cfg.ConfigFullName),
                        GeneratorConstants.StripGlobalPrefix(cfg.EntityFullName)));
                    continue;
                }

                if (!configsByEntity.TryGetValue(cfg.EntityFullName, out var list))
                {
                    list = [];
                    configsByEntity[cfg.EntityFullName] = list;
                }

                list.Add(cfg);
            }

            var validConfigByEntity = new Dictionary<string, ConfigCandidate>(StringComparer.Ordinal);
            foreach (var kv in configsByEntity)
            {
                if (kv.Value.Count > 1)
                {
                    var names = string.Join(", ", kv.Value
                        .Select(c => GeneratorConstants.StripGlobalPrefix(c.ConfigFullName))
                        .OrderBy(x => x, StringComparer.Ordinal));
                    foreach (var dup in kv.Value)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            GeneratorConstants.DuplicateEntityConfiguration,
                            dup.Location?.ToLocation(),
                            GeneratorConstants.StripGlobalPrefix(kv.Key),
                            names));
                    }
                }
                else
                {
                    validConfigByEntity[kv.Key] = kv.Value[0];
                }
            }

            // 2. Validate registries (SPM001, SPM004)
            var validRegistries = new List<RegistryCandidate>();
            foreach (var reg in allRegistries)
            {
                if (!reg.IsSealed || !reg.IsPartial || !reg.ImplementsInterface)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        GeneratorConstants.RegistryMustBeSealedPartialImplementingInterface,
                        reg.Location?.ToLocation(),
                        GeneratorConstants.StripGlobalPrefix(reg.FullTypeName)));
                    continue;
                }

                if (!reg.HasPublicConfigure)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        GeneratorConstants.RegistryMustDeclareConfigure,
                        reg.Location?.ToLocation(),
                        GeneratorConstants.StripGlobalPrefix(reg.FullTypeName)));
                    continue;
                }

                validRegistries.Add(reg);
            }

            if (validRegistries.Count > 0)
            {
                // 3. Collect entities by marker via O(1) manifest lookups on referenced assemblies + local entries
                var entitiesByMarker = CollectEntitiesByMarker(compilation, localDefault, localCustom);

                // 4. Detect entities matched by more than one registry in this compilation (SPM005)
                var registriesByEntity = new Dictionary<string, List<RegistryCandidate>>(StringComparer.Ordinal);
                var allMatchedEntities = new HashSet<string>(StringComparer.Ordinal);

                foreach (var reg in validRegistries)
                {
                    if (!entitiesByMarker.TryGetValue(reg.MarkerFullName, out var matchedEntities))
                    {
                        continue;
                    }

                    foreach (var entity in matchedEntities)
                    {
                        allMatchedEntities.Add(entity);
                        if (!registriesByEntity.TryGetValue(entity, out var regList))
                        {
                            regList = [];
                            registriesByEntity[entity] = regList;
                        }

                        regList.Add(reg);
                    }
                }

                foreach (var kv in registriesByEntity)
                {
                    if (kv.Value.Count > 1)
                    {
                        var regNames = string.Join(", ", kv.Value
                            .Select(r => GeneratorConstants.StripGlobalPrefix(r.FullTypeName))
                            .OrderBy(x => x, StringComparer.Ordinal));
                        foreach (var reg in kv.Value)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                GeneratorConstants.DuplicateEntityAcrossRegistries,
                                reg.Location?.ToLocation(),
                                GeneratorConstants.StripGlobalPrefix(kv.Key),
                                regNames));
                        }
                    }
                }

                // 5. Warn on orphan [EntityConfiguration<TEntity>] not matched by any registry (SPM007)
                foreach (var kv in validConfigByEntity)
                {
                    if (!allMatchedEntities.Contains(kv.Key))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            GeneratorConstants.OrphanEntityConfiguration,
                            kv.Value.Location?.ToLocation(),
                            GeneratorConstants.StripGlobalPrefix(kv.Value.ConfigFullName),
                            GeneratorConstants.StripGlobalPrefix(kv.Key)));
                    }
                }

                // 6. Emit partial registry explicit IEntityRegistry.Configure implementation
                foreach (var reg in validRegistries.OrderBy(r => r.FullTypeName, StringComparer.Ordinal))
                {
                    entitiesByMarker.TryGetValue(reg.MarkerFullName, out var matchedEntities);
                    EmitRegistryPartial(spc, reg, matchedEntities, validConfigByEntity);
                }

                // 7. Emit per-project aggregator for O(1) cross-assembly root discovery
                EmitProjectAggregator(spc, compilation.AssemblyName, validRegistries);
            }

            // 8. Root startup project extension method: options.AddGeneratedEntityRegistries()
            if (rootMode)
            {
                EmitRootExtension(spc, compilation, validRegistries);
            }
        });
    }

    private static RegistryCandidate? AnalyzeRegistry(GeneratorAttributeSyntaxContext ctx, bool isGeneric)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
            ctx.TargetNode is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        string markerFullName;
        if (!isGeneric)
        {
            markerFullName = "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName;
        }
        else
        {
            var attrData = ctx.Attributes.FirstOrDefault();
            if (attrData?.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } attrClass ||
                attrClass.TypeArguments[0] is not INamedTypeSymbol markerSymbol)
            {
                return null;
            }

            markerFullName = markerSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        var isPartial = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        var iRegistry = ctx.SemanticModel.Compilation.GetTypeByMetadataName(GeneratorConstants.IEntityRegistryMetadataName);
        var iBuilder = ctx.SemanticModel.Compilation.GetTypeByMetadataName(GeneratorConstants.IEntityRegistryBuilderMetadataName);

        var implementsInterface = iRegistry is not null &&
            symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iRegistry));

        var hasPublicConfigure = iBuilder is not null && symbol
            .GetMembers("Configure")
            .OfType<IMethodSymbol>()
            .Any(m =>
                m.DeclaredAccessibility == Accessibility.Public &&
                !m.IsStatic &&
                m.ReturnsVoid &&
                m.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, iBuilder));

        var containingTypes = ImmutableArray.CreateBuilder<string>();
        var parent = symbol.ContainingType;
        while (parent is not null)
        {
            var parentIsPartial = parent.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is TypeDeclarationSyntax tds &&
                tds.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
            if (!parentIsPartial)
            {
                isPartial = false;
            }

            var kind = parent.TypeKind switch
            {
                TypeKind.Struct => "struct",
                TypeKind.Interface => "interface",
                _ => "class"
            };
            containingTypes.Insert(0, $"partial {kind} {parent.Name}");
            parent = parent.ContainingType;
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : symbol.ContainingNamespace.ToDisplayString();

        return new RegistryCandidate(
            FullTypeName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SimpleName: symbol.Name,
            Namespace: ns,
            ContainingTypeDeclarations: containingTypes.ToImmutable(),
            MarkerFullName: markerFullName,
            IsSealed: symbol.IsSealed,
            IsPartial: isPartial,
            ImplementsInterface: implementsInterface,
            HasPublicConfigure: hasPublicConfigure,
            Location: LocationInfo.From(classDecl.Identifier.GetLocation()));
    }

    private static ConfigCandidate? AnalyzeConfiguration(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol ||
            ctx.TargetNode is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        var attrData = ctx.Attributes.FirstOrDefault();
        if (attrData?.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } attrClass ||
            attrClass.TypeArguments[0] is not INamedTypeSymbol entitySymbol)
        {
            return null;
        }

        var iConfigDef = ctx.SemanticModel.Compilation.GetTypeByMetadataName(GeneratorConstants.IEntityConfigurationMetadataName);
        var implementsExpected = iConfigDef is not null && symbol.AllInterfaces.Any(i =>
            i.IsGenericType &&
            SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, iConfigDef) &&
            i.TypeArguments.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], entitySymbol));

        var hasAccessibleCtor = symbol.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 &&
            c.DeclaredAccessibility == Accessibility.Public);

        var isValid = symbol.IsSealed && !symbol.IsAbstract && symbol.TypeParameters.Length == 0 &&
                      implementsExpected && hasAccessibleCtor;

        return new ConfigCandidate(
            ConfigFullName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityFullName: entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsValid: isValid,
            Location: LocationInfo.From(classDecl.Identifier.GetLocation()));
    }

    private static Dictionary<string, HashSet<string>> CollectEntitiesByMarker(
        Compilation compilation,
        ImmutableArray<LocalEntityEntry> localDefaultEntities,
        ImmutableArray<LocalEntityEntry> localCustomEntities)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(string markerFullName, string entityFullName)
        {
            if (!map.TryGetValue(markerFullName, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[markerFullName] = set;
            }

            set.Add(entityFullName);
        }

        // 1. Local built-in [Entity] and [assembly: DiscoverEntitiesBy<T>] classes
        foreach (var entry in localDefaultEntities)
        {
            Add(entry.MarkerFullName, entry.EntityFullName);
        }

        foreach (var entry in localCustomEntities)
        {
            Add(entry.MarkerFullName, entry.EntityFullName);
        }

        // 2. O(1) manifest lookup across referenced assemblies (ZERO assembly/namespace walking!)
        var entryAttrSymbol = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestEntryAttributeMetadataName);
        if (entryAttrSymbol is not null)
        {
            foreach (var refAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                var manifestMetadataName =
                    $"{GeneratorConstants.GeneratedNamespace}.{GeneratorConstants.ManifestPrefix}{GeneratorConstants.SanitizeAssemblyName(refAssembly.Name)}";
                var manifestType = refAssembly.GetTypeByMetadataName(manifestMetadataName);
                if (manifestType is null)
                {
                    continue;
                }

                foreach (var attr in manifestType.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, entryAttrSymbol) ||
                        attr.ConstructorArguments.Length != 2)
                    {
                        continue;
                    }

                    if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol markerType)
                    {
                        continue;
                    }

                    var markerFullName = markerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    var secondArg = attr.ConstructorArguments[1];
                    if (secondArg.Kind == TypedConstantKind.Array)
                    {
                        foreach (var item in secondArg.Values)
                        {
                            if (item.Value is INamedTypeSymbol entityType)
                            {
                                Add(markerFullName, entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                            }
                        }
                    }
                    else if (secondArg.Value is INamedTypeSymbol singleEntityType)
                    {
                        Add(markerFullName, singleEntityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
            }
        }

        return map;
    }

    private static void EmitRegistryPartial(
        SourceProductionContext spc,
        RegistryCandidate reg,
        HashSet<string>? matchedEntities,
        Dictionary<string, ConfigCandidate> validConfigByEntity)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var indent = string.Empty;
        if (!string.IsNullOrWhiteSpace(reg.Namespace))
        {
            sb.AppendLine($"namespace {reg.Namespace};");
            sb.AppendLine();
        }

        foreach (var outer in reg.ContainingTypeDeclarations)
        {
            sb.AppendLine($"{indent}{outer}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        sb.AppendLine($"{indent}partial class {reg.SimpleName}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    void global::{GeneratorConstants.IEntityRegistryMetadataName}.Configure(");
        sb.AppendLine($"{indent}        global::{GeneratorConstants.IEntityRegistryBuilderMetadataName} registry)");
        sb.AppendLine($"{indent}    {{");

        if (matchedEntities is { Count: > 0 })
        {
            foreach (var entityFullName in matchedEntities.OrderBy(e => e, StringComparer.Ordinal))
            {
                if (validConfigByEntity.TryGetValue(entityFullName, out var config))
                {
                    sb.AppendLine($"{indent}        registry.Map<{entityFullName}, {config.ConfigFullName}>();");
                }
                else
                {
                    sb.AppendLine($"{indent}        registry.Map<{entityFullName}>();");
                }
            }
        }

        sb.AppendLine($"{indent}        Configure(registry);");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");

        for (var i = reg.ContainingTypeDeclarations.Length - 1; i >= 0; i--)
        {
            indent = indent.Substring(0, indent.Length - 4);
            sb.AppendLine($"{indent}}}");
        }

        var cleanName = GeneratorConstants.StripGlobalPrefix(reg.FullTypeName);
        var hintName = $"{GeneratorConstants.SanitizeAssemblyName(cleanName)}.g.cs";
        spc.AddSource(hintName, sb.ToString());
    }

    private static void EmitProjectAggregator(
        SourceProductionContext spc,
        string? assemblyName,
        List<RegistryCandidate> validRegistries)
    {
        var sanitized = GeneratorConstants.SanitizeAssemblyName(assemblyName);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {GeneratorConstants.GeneratedNamespace};");
        sb.AppendLine();
        sb.AppendLine($"[global::{GeneratorConstants.EntityRegistryAggregatorAttributeMetadataName}]");

        var regTypeOfs = string.Join(", ", validRegistries
            .OrderBy(r => r.FullTypeName, StringComparer.Ordinal)
            .Select(r => $"typeof({r.FullTypeName})"));
        sb.AppendLine(
            $"[global::{GeneratorConstants.EntityRegistryAggregatorEntryAttributeMetadataName}({regTypeOfs})]");

        sb.AppendLine($"public static class {GeneratorConstants.AggregatorPrefix}{sanitized}");
        sb.AppendLine("{");
        sb.AppendLine("}");

        spc.AddSource("SharpPersistence.Migrator.EntityRegistryAggregator.g.cs", sb.ToString());
    }

    private static void EmitRootExtension(
        SourceProductionContext spc,
        Compilation compilation,
        List<RegistryCandidate> localRegistries)
    {
        var registryFullNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reg in localRegistries)
        {
            registryFullNames.Add(reg.FullTypeName);
        }

        // O(1) lookup of EntityRegistryAggregator_<Assembly> across all referenced assemblies
        var entryAttrSymbol = compilation.GetTypeByMetadataName(GeneratorConstants.EntityRegistryAggregatorEntryAttributeMetadataName);
        if (entryAttrSymbol is not null)
        {
            foreach (var refAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
            {
                var aggMetadataName =
                    $"{GeneratorConstants.GeneratedNamespace}.{GeneratorConstants.AggregatorPrefix}{GeneratorConstants.SanitizeAssemblyName(refAssembly.Name)}";
                var aggType = refAssembly.GetTypeByMetadataName(aggMetadataName);
                if (aggType is null)
                {
                    continue;
                }

                foreach (var attr in aggType.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, entryAttrSymbol) ||
                        attr.ConstructorArguments.Length != 1)
                    {
                        continue;
                    }

                    var arg = attr.ConstructorArguments[0];
                    if (arg.Kind == TypedConstantKind.Array)
                    {
                        foreach (var item in arg.Values)
                        {
                            if (item.Value is INamedTypeSymbol regType)
                            {
                                registryFullNames.Add(regType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                            }
                        }
                    }
                    else if (arg.Value is INamedTypeSymbol singleRegType)
                    {
                        registryFullNames.Add(singleRegType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine($"namespace {GeneratorConstants.GeneratedNamespace};");
        sb.AppendLine();
        sb.AppendLine("public static class GeneratedEntityRegistryExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    public static global::SharpPersistence.Migrator.Configuration.DbMigratorOptionsBuilder AddGeneratedEntityRegistries(");
        sb.AppendLine("        this global::SharpPersistence.Migrator.Configuration.DbMigratorOptionsBuilder builder)");
        sb.AppendLine("    {");

        foreach (var regFullName in registryFullNames.OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.AppendLine($"        builder.AddRegistry<{regFullName}>();");
        }

        sb.AppendLine("        return builder;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("SharpPersistence.Migrator.GeneratedEntityRegistries.g.cs", sb.ToString());
    }
}
