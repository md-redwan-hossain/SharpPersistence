using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigratorUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        GeneratorConstants.RequiredColumnInsideOptionalOwnedNavigation,
        GeneratorConstants.InvalidEntityReplacement,
        GeneratorConstants.InvalidRegistryEntityReference
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            var replacementBuilderType = compilation.GetTypeByMetadataName(GeneratorConstants.EntityTypeReplacementBuilderMetadataName);
            var defaultEntityAttr = compilation.GetTypeByMetadataName(GeneratorConstants.DefaultEntityAttributeMetadataName);
            var genericEntityAttr = compilation.GetTypeByMetadataName(GeneratorConstants.GenericEntityAttributeMetadataName);
            var defaultRegistryAttr = compilation.GetTypeByMetadataName(GeneratorConstants.EntityRegistryAttributeMetadataName);
            var genericRegistryAttr = compilation.GetTypeByMetadataName(GeneratorConstants.GenericEntityRegistryAttributeMetadataName);
            var registryBuilderInterface = compilation.GetTypeByMetadataName(GeneratorConstants.IEntityRegistryBuilderMetadataName);
            var nonGenericEntityBuilderType = compilation.GetTypeByMetadataName(GeneratorConstants.NonGenericEntityBuilderMetadataName);
            var entityBuilderDef = compilation.GetTypeByMetadataName(GeneratorConstants.EntityBuilderMetadataName);
            var ownedBuilderDef = compilation.GetTypeByMetadataName(GeneratorConstants.OwnedEntityBuilderMetadataName);
            var columnBuilderBaseDef = compilation.GetTypeByMetadataName(GeneratorConstants.ColumnBuilderBaseMetadataName);

            if (replacementBuilderType is not null || columnBuilderBaseDef is not null)
            {
                compilationContext.RegisterOperationAction(opCtx =>
                {
                    var invocation = (IInvocationOperation)opCtx.Operation;
                    var method = invocation.TargetMethod;

                    // SPM015: EntityTypeReplacementBuilder.Replace<TFrom, TTo>()
                    if (replacementBuilderType is not null &&
                        method.Name == KnownNames.Methods.Replace &&
                        method.TypeArguments.Length == 2 &&
                        SymbolEqualityComparer.Default.Equals(method.ContainingType, replacementBuilderType))
                    {
                        AnalyzeEntityReplacement(opCtx, invocation, method, defaultEntityAttr, genericEntityAttr);
                        return;
                    }

                    // SPM014: ColumnBuilderBase<T>.IsRequired(true) inside optional OwnsOne(...)
                    if (columnBuilderBaseDef is not null &&
                        method.Name == KnownNames.Methods.IsRequired &&
                        RoslynSymbolHelper.InheritsGenericDefinition(method.ContainingType, columnBuilderBaseDef))
                    {
                        AnalyzeIsRequiredInsideOwnsOne(opCtx, invocation, entityBuilderDef, ownedBuilderDef);
                    }
                }, OperationKind.Invocation);
            }

            if (registryBuilderInterface is not null && (defaultRegistryAttr is not null || genericRegistryAttr is not null))
            {
                compilationContext.RegisterSymbolStartAction(symbolStartCtx =>
                {
                    if (symbolStartCtx.Symbol is not INamedTypeSymbol registryClass ||
                        !TryGetRegistryGroup(registryClass, defaultRegistryAttr, genericRegistryAttr, out var registryGroup))
                    {
                        return;
                    }

                    symbolStartCtx.RegisterOperationBlockAction(blockCtx =>
                    {
                        AnalyzeRegistryConfigureBlock(
                            blockCtx,
                            registryClass,
                            registryGroup,
                            registryBuilderInterface,
                            nonGenericEntityBuilderType,
                            defaultEntityAttr,
                            genericEntityAttr);
                    });
                }, SymbolKind.NamedType);
            }
        });
    }

    private static void AnalyzeEntityReplacement(
        OperationAnalysisContext opCtx,
        IInvocationOperation invocation,
        IMethodSymbol method,
        INamedTypeSymbol? defaultEntityAttr,
        INamedTypeSymbol? genericEntityAttr)
    {
        var sourceType = method.TypeArguments[0];
        var targetType = method.TypeArguments[1];

        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            opCtx.ReportDiagnostic(Diagnostic.Create(
                GeneratorConstants.InvalidEntityReplacement,
                invocation.Syntax.GetLocation(),
                $"Cannot replace entity type '{sourceType.Name}' with itself in ReplaceEntityTypes."));
            return;
        }

        if (targetType is INamedTypeSymbol targetNamed &&
            RoslynSymbolHelper.IsMarkedReadOnly(targetNamed, defaultEntityAttr, genericEntityAttr))
        {
            opCtx.ReportDiagnostic(Diagnostic.Create(
                GeneratorConstants.InvalidEntityReplacement,
                invocation.Syntax.GetLocation(),
                $"Cannot replace entity type '{sourceType.Name}' with '{targetType.Name}' because '{targetType.Name}' is marked ReadOnly = true."));
        }
    }

    private static void AnalyzeIsRequiredInsideOwnsOne(
        OperationAnalysisContext opCtx,
        IInvocationOperation invocation,
        INamedTypeSymbol? entityBuilderDef,
        INamedTypeSymbol? ownedBuilderDef)
    {
        // Only flag IsRequired() or IsRequired(true); IsRequired(false) is valid
        if (invocation.Arguments.Length == 1 &&
            invocation.Arguments[0].Value.ConstantValue is { HasValue: true, Value: false })
        {
            return;
        }

        // Walk enclosing operations looking for any OwnsOne(...) lambda whose navigation property is nullable
        for (var current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (current is not IInvocationOperation enclosingCall)
            {
                continue;
            }

            var enclosingMethod = enclosingCall.TargetMethod;
            if (enclosingMethod.Name != KnownNames.Methods.OwnsOne || enclosingCall.Arguments.Length < 2)
            {
                continue;
            }

            var containingDef = enclosingMethod.ContainingType?.OriginalDefinition;
            var isOwnsOneBuilder =
                (entityBuilderDef is not null && SymbolEqualityComparer.Default.Equals(containingDef, entityBuilderDef)) ||
                (ownedBuilderDef is not null && SymbolEqualityComparer.Default.Equals(containingDef, ownedBuilderDef));

            if (!isOwnsOneBuilder)
            {
                continue;
            }

            // Ensure the IsRequired() call is actually inside the configure lambda (argument 1), not the navigation expression
            if (!IsDescendantOf(invocation, enclosingCall.Arguments[1]))
            {
                continue;
            }

            var navProp = ExtractNavigationProperty(enclosingCall.Arguments[0].Value);
            if (navProp is not null && IsNullableProperty(navProp))
            {
                opCtx.ReportDiagnostic(Diagnostic.Create(
                    GeneratorConstants.RequiredColumnInsideOptionalOwnedNavigation,
                    invocation.Syntax.GetLocation(),
                    navProp.Name));
                return;
            }
        }
    }

    private static IPropertySymbol? ExtractNavigationProperty(IOperation navigationArgValue)
    {
        var queue = new Queue<IOperation>();
        queue.Enqueue(navigationArgValue);

        while (queue.Count > 0)
        {
            var op = queue.Dequeue();
            if (op is IPropertyReferenceOperation propRef)
            {
                return propRef.Property;
            }

            foreach (var child in op.ChildOperations)
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }

    private static bool IsNullableProperty(IPropertySymbol prop)
    {
        if (prop.NullableAnnotation == NullableAnnotation.Annotated)
        {
            return true;
        }

        return prop.Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }

    private static bool IsDescendantOf(IOperation node, IOperation ancestor)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static void AnalyzeRegistryConfigureBlock(
        OperationBlockAnalysisContext blockCtx,
        INamedTypeSymbol registryClass,
        ITypeSymbol? registryGroup,
        INamedTypeSymbol registryBuilderInterface,
        INamedTypeSymbol? nonGenericEntityBuilderType,
        INamedTypeSymbol? defaultEntityAttr,
        INamedTypeSymbol? genericEntityAttr)
    {
        if (blockCtx.OwningSymbol is not IMethodSymbol method ||
            method.Name != KnownNames.Methods.Configure ||
            method.Parameters.Length != 1 ||
            !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, registryBuilderInterface))
        {
            return;
        }

        var mapInvocations = new List<(IInvocationOperation Invocation, INamedTypeSymbol EntityType)>();
        var entityInvocations = new List<(IInvocationOperation Invocation, INamedTypeSymbol EntityType)>();
        var explicitlyMapped = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var root in blockCtx.OperationBlocks)
        {
            foreach (var op in root.DescendantsAndSelf())
            {
                if (op is not IInvocationOperation inv)
                {
                    continue;
                }

                var target = inv.TargetMethod;
                if (target.Name == KnownNames.Methods.Map &&
                    target.TypeArguments.Length is 1 or 2 &&
                    SymbolEqualityComparer.Default.Equals(target.ContainingType, registryBuilderInterface) &&
                    target.TypeArguments[0] is INamedTypeSymbol mappedType)
                {
                    mapInvocations.Add((inv, mappedType));
                }
                else if (nonGenericEntityBuilderType is not null &&
                         target.Name == KnownNames.Methods.Entity &&
                         target.TypeArguments.Length == 1 &&
                         SymbolEqualityComparer.Default.Equals(target.ContainingType, nonGenericEntityBuilderType) &&
                         target.TypeArguments[0] is INamedTypeSymbol entityType &&
                         IsInsideRegistryConfigureEntities(inv, registryBuilderInterface))
                {
                    entityInvocations.Add((inv, entityType));
                }
            }
        }

        foreach (var (inv, mappedType) in mapInvocations)
        {
            if (HasMismatchedEntityAttribute(mappedType, registryGroup, defaultEntityAttr, genericEntityAttr))
            {
                blockCtx.ReportDiagnostic(Diagnostic.Create(
                    GeneratorConstants.InvalidRegistryEntityReference,
                    inv.Syntax.GetLocation(),
                    $"Entity type '{mappedType.Name}' is marked for a different entity group and does not belong to registry '{registryClass.Name}'."));
            }
            else
            {
                explicitlyMapped.Add(mappedType);
            }
        }

        foreach (var (inv, entityType) in entityInvocations)
        {
            if (entityType.TypeKind != TypeKind.Class || entityType.IsAbstract)
            {
                continue;
            }

            if (explicitlyMapped.Contains(entityType))
            {
                continue;
            }

            if (MatchesRegistryEntityAttribute(entityType, registryGroup, defaultEntityAttr, genericEntityAttr))
            {
                continue;
            }

            blockCtx.ReportDiagnostic(Diagnostic.Create(
                GeneratorConstants.InvalidRegistryEntityReference,
                inv.Syntax.GetLocation(),
                $"Concrete entity type '{entityType.Name}' does not belong to registry '{registryClass.Name}'."));
        }
    }

    private static bool TryGetRegistryGroup(
        INamedTypeSymbol registryClass,
        INamedTypeSymbol? defaultRegistryAttr,
        INamedTypeSymbol? genericRegistryAttr,
        out ITypeSymbol? groupType)
    {
        groupType = null;
        foreach (var attr in registryClass.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
            {
                continue;
            }

            if (defaultRegistryAttr is not null && SymbolEqualityComparer.Default.Equals(attrClass, defaultRegistryAttr))
            {
                groupType = null;
                return true;
            }

            if (genericRegistryAttr is not null &&
                attrClass.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(attrClass.OriginalDefinition, genericRegistryAttr))
            {
                groupType = attrClass.TypeArguments[0];
                return true;
            }
        }

        return false;
    }

    private static bool MatchesRegistryEntityAttribute(
        INamedTypeSymbol entityType,
        ITypeSymbol? registryGroup,
        INamedTypeSymbol? defaultEntityAttr,
        INamedTypeSymbol? genericEntityAttr)
    {
        foreach (var attr in entityType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
            {
                continue;
            }

            if (registryGroup is null &&
                defaultEntityAttr is not null &&
                SymbolEqualityComparer.Default.Equals(attrClass, defaultEntityAttr))
            {
                return true;
            }

            if (registryGroup is not null &&
                genericEntityAttr is not null &&
                attrClass.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(attrClass.OriginalDefinition, genericEntityAttr) &&
                SymbolEqualityComparer.Default.Equals(attrClass.TypeArguments[0], registryGroup))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMismatchedEntityAttribute(
        INamedTypeSymbol entityType,
        ITypeSymbol? registryGroup,
        INamedTypeSymbol? defaultEntityAttr,
        INamedTypeSymbol? genericEntityAttr)
    {
        var hasAnyEntityAttr = false;
        foreach (var attr in entityType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
            {
                continue;
            }

            var isDefault = defaultEntityAttr is not null && SymbolEqualityComparer.Default.Equals(attrClass, defaultEntityAttr);
            var isGeneric = genericEntityAttr is not null &&
                            attrClass.IsGenericType &&
                            SymbolEqualityComparer.Default.Equals(attrClass.OriginalDefinition, genericEntityAttr);

            if (!isDefault && !isGeneric)
            {
                continue;
            }

            hasAnyEntityAttr = true;
            if (registryGroup is null && isDefault)
            {
                return false;
            }

            if (registryGroup is not null && isGeneric && SymbolEqualityComparer.Default.Equals(attrClass.TypeArguments[0], registryGroup))
            {
                return false;
            }
        }

        return hasAnyEntityAttr;
    }

    private static bool IsInsideRegistryConfigureEntities(
        IInvocationOperation invocation,
        INamedTypeSymbol registryBuilderInterface)
    {
        for (var current = invocation.Parent; current is not null; current = current.Parent)
        {
            if (current is IInvocationOperation enclosing &&
                enclosing.TargetMethod.Name == KnownNames.Methods.ConfigureEntities &&
                SymbolEqualityComparer.Default.Equals(enclosing.TargetMethod.ContainingType, registryBuilderInterface))
            {
                return true;
            }
        }

        return false;
    }
}
