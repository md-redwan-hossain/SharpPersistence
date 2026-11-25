using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpPersistence.Migrator.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigratorUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        GeneratorConstants.RequiredColumnInsideOptionalOwnedNavigation,
        GeneratorConstants.InvalidEntityStubMapping
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            var stubBuilderType = compilation.GetTypeByMetadataName(GeneratorConstants.EntityStubBuilderMetadataName);
            var entityMarkerBase = compilation.GetTypeByMetadataName(GeneratorConstants.EntityMarkerAttributeMetadataName);
            var entityBuilderDef = compilation.GetTypeByMetadataName(GeneratorConstants.EntityBuilderMetadataName);
            var ownedBuilderDef = compilation.GetTypeByMetadataName(GeneratorConstants.OwnedEntityBuilderMetadataName);
            var columnBuilderBaseDef = compilation.GetTypeByMetadataName(GeneratorConstants.ColumnBuilderBaseMetadataName);

            if (stubBuilderType is null && columnBuilderBaseDef is null)
            {
                return;
            }

            compilationContext.RegisterOperationAction(opCtx =>
            {
                var invocation = (IInvocationOperation)opCtx.Operation;
                var method = invocation.TargetMethod;

                // SPM015: EntityStubBuilder.Map<TRef, TOwner>()
                if (stubBuilderType is not null &&
                    method.Name == "Map" &&
                    method.TypeArguments.Length == 2 &&
                    SymbolEqualityComparer.Default.Equals(method.ContainingType, stubBuilderType))
                {
                    AnalyzeStubMapping(opCtx, invocation, method, entityMarkerBase);
                    return;
                }

                // SPM014: ColumnBuilderBase<T>.IsRequired(true) inside optional OwnsOne(...)
                if (columnBuilderBaseDef is not null &&
                    method.Name == "IsRequired" &&
                    RoslynSymbolHelper.InheritsGenericDefinition(method.ContainingType, columnBuilderBaseDef))
                {
                    AnalyzeIsRequiredInsideOwnsOne(opCtx, invocation, entityBuilderDef, ownedBuilderDef);
                }
            }, OperationKind.Invocation);
        });
    }

    private static void AnalyzeStubMapping(
        OperationAnalysisContext opCtx,
        IInvocationOperation invocation,
        IMethodSymbol method,
        INamedTypeSymbol? entityMarkerBase)
    {
        var refType = method.TypeArguments[0];
        var ownerType = method.TypeArguments[1];

        if (SymbolEqualityComparer.Default.Equals(refType, ownerType))
        {
            opCtx.ReportDiagnostic(Diagnostic.Create(
                GeneratorConstants.InvalidEntityStubMapping,
                invocation.Syntax.GetLocation(),
                $"Cannot map entity stub '{refType.Name}' to itself in ConfigureEntityStubs."));
            return;
        }

        if (ownerType is INamedTypeSymbol ownerNamed &&
            entityMarkerBase is not null &&
            RoslynSymbolHelper.IsMarkedReadOnly(ownerNamed, entityMarkerBase))
        {
            opCtx.ReportDiagnostic(Diagnostic.Create(
                GeneratorConstants.InvalidEntityStubMapping,
                invocation.Syntax.GetLocation(),
                $"Cannot map entity stub '{refType.Name}' to '{ownerType.Name}' because '{ownerType.Name}' is marked ReadOnly = true."));
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
            if (enclosingMethod.Name != "OwnsOne" || enclosingCall.Arguments.Length < 2)
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
}
