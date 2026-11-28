using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.Generators;

[Generator]
public sealed class EntityRegistryGenerator : IIncrementalGenerator
{
    private readonly record struct EntityGroupEntry
    {
        public required string GroupFullName { get; init; }
        public required string EntityFullName { get; init; }
    }

    private sealed record RegistryCandidate
    {
        public required string FullTypeName { get; init; }
        public required string SimpleName { get; init; }
        public required string? Namespace { get; init; }
        public required EquatableArray<string> ContainingTypeDeclarations { get; init; }
        public required string GroupFullName { get; init; }
        public required bool IsSealed { get; init; }
        public required bool IsPartial { get; init; }
        public required bool ImplementsInterface { get; init; }
        public required bool HasPublicConfigure { get; init; }
        public required SourceLocationInfo? Location { get; init; }
    }

    private readonly record struct ConfigCandidate
    {
        public required string ConfigFullName { get; init; }
        public required string EntityFullName { get; init; }
        public required bool IsValid { get; init; }
        public required SourceLocationInfo? Location { get; init; }
    }

    private sealed record CompilationRegistryMeta
    {
        public required string? AssemblyName { get; init; }
        public required bool HasMigratorReference { get; init; }
        public required bool HasDisableAttribute { get; init; }
        public required bool HasRootAttribute { get; init; }
        public required EquatableArray<EntityGroupEntry> ReferencedManifestEntries { get; init; }
        public required EquatableArray<string> ReferencedAggregatorRegistries { get; init; }
    }

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

        var compilationMeta = context.CompilationProvider
            .Select(static (compilation, ct) => ExtractCompilationMeta(compilation, ct));

        var defaultRegistries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.EntityRegistryAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeRegistry(ctx, isGeneric: false))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<RegistryCandidate>(arr));

        var genericRegistries = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.GenericEntityRegistryAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeRegistry(ctx, isGeneric: true))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<RegistryCandidate>(arr));

        var configurations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.EntityConfigurationAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => AnalyzeConfiguration(ctx))
            .Where(static x => x.HasValue)
            .Select(static (x, _) => x!.Value)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<ConfigCandidate>(arr));

        var localDefaultEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.DefaultEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ExtractDefaultLocalEntity(ctx))
            .Where(static x => x.HasValue)
            .Select(static (x, _) => x!.Value)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<EntityGroupEntry>(arr));

        var localGroupedEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.GenericEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => ExtractGroupedLocalEntities(ctx))
            .Where(static arr => arr.Length > 0)
            .SelectMany(static (arr, _) => arr)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<EntityGroupEntry>(arr));

        var pipeline = compilationMeta
            .Combine(defaultRegistries.Combine(genericRegistries).Combine(configurations))
            .Combine(localDefaultEntities.Combine(localGroupedEntities))
            .Combine(isRoot)
            .Combine(isDisabled);

        context.RegisterSourceOutput(pipeline, static (spc, input) =>
        {
            if (input.Right)
            {
                return;
            }

            var meta = input.Left.Left.Left.Left;
            if (meta.HasDisableAttribute || !meta.HasMigratorReference)
            {
                return;
            }

            Execute(
                spc,
                meta,
                input.Left.Left.Left.Right.Left.Left,
                input.Left.Left.Left.Right.Left.Right,
                input.Left.Left.Left.Right.Right,
                input.Left.Left.Right.Left,
                input.Left.Left.Right.Right,
                input.Left.Right || meta.HasRootAttribute);
        });
    }

    private static CompilationRegistryMeta ExtractCompilationMeta(Compilation compilation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasMigrator = compilation.GetTypeByMetadataName(GeneratorConstants.IEntityRegistryMetadataName) is not null;
        var hasDisable = GeneratorConstants.HasAssemblyAttribute(compilation, GeneratorConstants.DisableEntityDiscoveryAttributeMetadataName);
        if (!hasMigrator || hasDisable)
        {
            return new CompilationRegistryMeta
            {
                AssemblyName = compilation.AssemblyName,
                HasMigratorReference = hasMigrator,
                HasDisableAttribute = hasDisable,
                HasRootAttribute = false,
                ReferencedManifestEntries = EquatableArray<EntityGroupEntry>.Empty,
                ReferencedAggregatorRegistries = EquatableArray<string>.Empty
            };
        }

        var manifestEntries = ImmutableArray.CreateBuilder<EntityGroupEntry>();
        var aggRegistries = ImmutableArray.CreateBuilder<string>();

        var manifestEntryAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityManifestEntryAttributeMetadataName);
        var aggEntryAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityRegistryAggregatorEntryAttributeMetadataName);

        foreach (var refAsm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsFrameworkAssemblyName(refAsm.Name))
            {
                continue;
            }

            var sanitized = GeneratorConstants.SanitizeAssemblyName(refAsm.Name);

            if (manifestEntryAttr is not null)
            {
                var manifestType = refAsm.GetTypeByMetadataName(
                    $"{GeneratorConstants.GeneratedNamespace}.{GeneratorConstants.ManifestPrefix}{sanitized}");
                if (manifestType is not null)
                {
                    CollectManifestEntries(manifestType, manifestEntryAttr, manifestEntries);
                }
            }

            if (aggEntryAttr is not null)
            {
                var aggType = refAsm.GetTypeByMetadataName(
                    $"{GeneratorConstants.GeneratedNamespace}.{GeneratorConstants.AggregatorPrefix}{sanitized}");
                if (aggType is not null)
                {
                    CollectAggregatorRegistries(aggType, aggEntryAttr, aggRegistries);
                }
            }
        }

        return new CompilationRegistryMeta
        {
            AssemblyName = compilation.AssemblyName,
            HasMigratorReference = true,
            HasDisableAttribute = false,
            HasRootAttribute = GeneratorConstants.HasAssemblyAttribute(compilation, GeneratorConstants.EntityRegistryRootAttributeMetadataName),
            ReferencedManifestEntries = new EquatableArray<EntityGroupEntry>(manifestEntries.ToImmutable()),
            ReferencedAggregatorRegistries = new EquatableArray<string>(aggRegistries.ToImmutable())
        };
    }

    private static bool IsFrameworkAssemblyName(string? name) =>
        string.IsNullOrWhiteSpace(name) ||
        name!.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase);

    private static void CollectManifestEntries(
        INamedTypeSymbol manifestType,
        INamedTypeSymbol entryAttrSymbol,
        ImmutableArray<EntityGroupEntry>.Builder target)
    {
        foreach (var attr in manifestType.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, entryAttrSymbol) ||
                attr.ConstructorArguments.Length != 2 ||
                attr.ConstructorArguments[0].Value is not INamedTypeSymbol groupType)
            {
                continue;
            }

            var groupFullName = groupType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var secondArg = attr.ConstructorArguments[1];
            if (secondArg.Kind == TypedConstantKind.Array)
            {
                foreach (var item in secondArg.Values)
                {
                    if (item.Value is INamedTypeSymbol entityType)
                    {
                        target.Add(new EntityGroupEntry
                        {
                            GroupFullName = groupFullName,
                            EntityFullName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        });
                    }
                }
            }
            else if (secondArg.Value is INamedTypeSymbol singleEntity)
            {
                target.Add(new EntityGroupEntry
                {
                    GroupFullName = groupFullName,
                    EntityFullName = singleEntity.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                });
            }
        }
    }

    private static void CollectAggregatorRegistries(
        INamedTypeSymbol aggType,
        INamedTypeSymbol entryAttrSymbol,
        ImmutableArray<string>.Builder target)
    {
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
                        target.Add(regType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
            }
            else if (arg.Value is INamedTypeSymbol singleReg)
            {
                target.Add(singleReg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }
    }

    private static EntityGroupEntry? ExtractDefaultLocalEntity(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol || symbol.IsAbstract || symbol.TypeParameters.Length > 0)
        {
            return null;
        }

        return new EntityGroupEntry
        {
            GroupFullName = "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName,
            EntityFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };
    }

    private static EquatableArray<EntityGroupEntry> ExtractGroupedLocalEntities(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol || symbol.IsAbstract || symbol.TypeParameters.Length > 0)
        {
            return EquatableArray<EntityGroupEntry>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<EntityGroupEntry>(ctx.Attributes.Length);
        var entityFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var attr in ctx.Attributes)
        {
            if (attr.AttributeClass is { IsGenericType: true, TypeArguments.Length: 1 } ac &&
                ac.TypeArguments[0] is INamedTypeSymbol groupSym)
            {
                builder.Add(new EntityGroupEntry
                {
                    GroupFullName = groupSym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    EntityFullName = entityFullName
                });
            }
        }

        return new EquatableArray<EntityGroupEntry>(builder.ToImmutable());
    }

    private static RegistryCandidate? AnalyzeRegistry(GeneratorAttributeSyntaxContext ctx, bool isGeneric)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol || ctx.TargetNode is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        string groupFullName;
        if (isGeneric)
        {
            var attrData = ctx.Attributes.FirstOrDefault();
            if (attrData?.AttributeClass is not { IsGenericType: true, TypeArguments.Length: 1 } ac ||
                ac.TypeArguments[0] is not INamedTypeSymbol groupSymbol)
            {
                return null;
            }

            groupFullName = groupSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        else
        {
            groupFullName = "global::" + GeneratorConstants.DefaultEntityAttributeMetadataName;
        }

        var compilation = ctx.SemanticModel.Compilation;
        var iRegistry = compilation.GetTypeByMetadataName(GeneratorConstants.IEntityRegistryMetadataName);
        var iBuilder = compilation.GetTypeByMetadataName(GeneratorConstants.IEntityRegistryBuilderMetadataName);
        var implementsInterface = iRegistry is not null &&
            symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, iRegistry));

        var hasPublicConfigure = iBuilder is not null && symbol.GetMembers(KnownNames.Methods.Configure)
            .OfType<IMethodSymbol>()
            .Any(m => !m.IsStatic &&
                      m.ExplicitInterfaceImplementations.IsEmpty &&
                      m.ReturnsVoid &&
                      m.Parameters.Length == 1 &&
                      SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, iBuilder));

        var containingDecls = ImmutableArray.CreateBuilder<string>();
        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            var kind = outer.TypeKind switch
            {
                TypeKind.Struct => "partial struct",
                TypeKind.Interface => "partial interface",
                _ => "partial class"
            };
            containingDecls.Insert(0, $"{kind} {outer.Name}");
        }

        return new RegistryCandidate
        {
            FullTypeName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SimpleName = symbol.Name,
            Namespace = symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            ContainingTypeDeclarations = new EquatableArray<string>(containingDecls.ToImmutable()),
            GroupFullName = groupFullName,
            IsSealed = symbol.IsSealed && !symbol.IsAbstract,
            IsPartial = RoslynSymbolHelper.IsDeclaredPartial(symbol),
            ImplementsInterface = implementsInterface,
            HasPublicConfigure = hasPublicConfigure,
            Location = SourceLocationInfo.From(classDecl.Identifier.GetLocation())
        };
    }

    private static ConfigCandidate? AnalyzeConfiguration(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol || ctx.TargetNode is not ClassDeclarationSyntax classDecl)
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
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        var isValid = symbol.IsSealed && !symbol.IsAbstract && symbol.TypeParameters.Length == 0 &&
                      implementsExpected && hasAccessibleCtor;

        return new ConfigCandidate
        {
            ConfigFullName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityFullName = entitySymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsValid = isValid,
            Location = SourceLocationInfo.From(classDecl.Identifier.GetLocation())
        };
    }

    private static void Execute(
        SourceProductionContext spc,
        CompilationRegistryMeta meta,
        EquatableArray<RegistryCandidate> defaultRegs,
        EquatableArray<RegistryCandidate> genericRegs,
        EquatableArray<ConfigCandidate> configCandidates,
        EquatableArray<EntityGroupEntry> localDefault,
        EquatableArray<EntityGroupEntry> localGrouped,
        bool rootMode)
    {
        var configsByEntity = new Dictionary<string, List<ConfigCandidate>>(StringComparer.Ordinal);
        foreach (var cfg in configCandidates)
        {
            if (!cfg.IsValid)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    GeneratorConstants.ConfigurationInvalid,
                    cfg.Location?.ToLocation() ?? Location.None,
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
                        dup.Location?.ToLocation() ?? Location.None,
                        GeneratorConstants.StripGlobalPrefix(kv.Key),
                        names));
                }
            }
            else
            {
                validConfigByEntity[kv.Key] = kv.Value[0];
            }
        }

        var validRegistries = new List<RegistryCandidate>();
        foreach (var reg in defaultRegs.Concat(genericRegs))
        {
            if (!reg.IsSealed || !reg.IsPartial || !reg.ImplementsInterface)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    GeneratorConstants.RegistryMustBeSealedPartialImplementingInterface,
                    reg.Location?.ToLocation() ?? Location.None,
                    GeneratorConstants.StripGlobalPrefix(reg.FullTypeName)));
                continue;
            }

            validRegistries.Add(reg);
        }

        if (validRegistries.Count > 0)
        {
            var entitiesByGroup = BuildEntitiesByGroup(meta.ReferencedManifestEntries, localDefault, localGrouped);
            var registriesByEntity = new Dictionary<string, List<RegistryCandidate>>(StringComparer.Ordinal);
            var allMatchedEntities = new HashSet<string>(StringComparer.Ordinal);

            foreach (var reg in validRegistries)
            {
                if (!entitiesByGroup.TryGetValue(reg.GroupFullName, out var matched))
                {
                    continue;
                }

                foreach (var entity in matched)
                {
                    allMatchedEntities.Add(entity);
                    if (!registriesByEntity.TryGetValue(entity, out var regs))
                    {
                        regs = [];
                        registriesByEntity[entity] = regs;
                    }

                    regs.Add(reg);
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
                            reg.Location?.ToLocation() ?? Location.None,
                            GeneratorConstants.StripGlobalPrefix(kv.Key),
                            regNames));
                    }
                }
            }

            foreach (var kv in validConfigByEntity)
            {
                if (!allMatchedEntities.Contains(kv.Key))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        GeneratorConstants.OrphanEntityConfiguration,
                        kv.Value.Location?.ToLocation() ?? Location.None,
                        GeneratorConstants.StripGlobalPrefix(kv.Value.ConfigFullName),
                        GeneratorConstants.StripGlobalPrefix(kv.Key)));
                }
            }

            foreach (var reg in validRegistries)
            {
                entitiesByGroup.TryGetValue(reg.GroupFullName, out var matched);
                EmitRegistryPartial(spc, reg, matched, validConfigByEntity);
            }

            EmitProjectAggregator(spc, meta.AssemblyName, validRegistries);
        }

        if (rootMode)
        {
            EmitRootExtension(spc, meta.ReferencedAggregatorRegistries, validRegistries);
        }
    }

    private static Dictionary<string, HashSet<string>> BuildEntitiesByGroup(
        EquatableArray<EntityGroupEntry> referencedEntries,
        EquatableArray<EntityGroupEntry> localDefault,
        EquatableArray<EntityGroupEntry> localGrouped)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(EquatableArray<EntityGroupEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (!map.TryGetValue(entry.GroupFullName, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    map[entry.GroupFullName] = set;
                }

                set.Add(entry.EntityFullName);
            }
        }

        Add(localDefault);
        Add(localGrouped);
        Add(referencedEntries);
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

        if (reg.HasPublicConfigure)
        {
            sb.AppendLine($"{indent}        Configure(registry);");
        }

        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");

        for (var i = reg.ContainingTypeDeclarations.Length - 1; i >= 0; i--)
        {
            indent = indent.Substring(0, indent.Length - 4);
            sb.AppendLine($"{indent}}}");
        }

        var cleanName = GeneratorConstants.StripGlobalPrefix(reg.FullTypeName);
        spc.AddSource($"{GeneratorConstants.SanitizeAssemblyName(cleanName)}.g.cs", sb.ToString());
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
        EquatableArray<string> referencedRegistries,
        List<RegistryCandidate> localRegistries)
    {
        var registryFullNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reg in localRegistries)
        {
            registryFullNames.Add(reg.FullTypeName);
        }

        foreach (var reg in referencedRegistries)
        {
            registryFullNames.Add(reg);
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
