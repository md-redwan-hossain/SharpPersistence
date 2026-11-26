using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpPersistence.Migrator.Constants;

namespace SharpPersistence.Migrator.Generators;

[Generator]
public sealed class DbContextBindingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var boundDbContexts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.BindEntityRegistryAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => DbContextBindingAnalyzer.AnalyzeBoundDbContext(ctx, ct))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        var localDefaultEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.DefaultEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => DbContextBindingAnalyzer.ExtractLocalDefaultEntity(ctx))
            .Where(static x => x.HasValue)
            .Select(static (x, _) => x!.Value)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<LocalDiscoveredEntity>(arr));

        var localGroupedEntities = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.GenericEntityAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => DbContextBindingAnalyzer.ExtractLocalGroupedEntities(ctx))
            .Where(static arr => arr.Length > 0)
            .SelectMany(static (arr, _) => arr)
            .Collect()
            .Select(static (arr, _) => new EquatableArray<LocalDiscoveredEntity>(arr));

        var localEntities = localDefaultEntities.Combine(localGroupedEntities);

        var finalizedModels = boundDbContexts
            .Combine(localEntities)
            .Select(static (pair, _) =>
                DbContextBindingAnalyzer.FinalizeModel(pair.Left, pair.Right.Left, pair.Right.Right));

        var orphanItemDiagnostics = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.EntityRegistryItemAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => DbContextBindingAnalyzer.AnalyzeOrphanItemClass(ctx))
            .Where(static arr => arr.Length > 0);

        context.RegisterSourceOutput(finalizedModels, static (spc, model) =>
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

    private static void ReportDiagnostic(SourceProductionContext spc, PendingDiagnostic diag)
    {
        var location = diag.Location?.ToLocation() ?? Location.None;
        switch (diag.DiagnosticId)
        {
            case DiagnosticIds.DbContextMustBeSealedPartial:
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DbContextMustBeSealedPartial, location, diag.Arg0));
                break;
            case DiagnosticIds.InvalidRegistryItemCombination:
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.InvalidRegistryItemCombination, location, diag.Arg0, diag.Arg1));
                break;
            case DiagnosticIds.DuplicateDbContextBindingAttribute:
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DuplicateDbContextBindingAttribute, location, diag.Arg0, diag.Arg1, diag.Arg2));
                break;
            case DiagnosticIds.OrphanEntityRegistryItem:
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.OrphanEntityRegistryItem, location, diag.Arg0, diag.Arg1, diag.Arg2));
                break;
            case DiagnosticIds.DbContextPropertyNameCollision:
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DbContextPropertyNameCollision, location, diag.Arg0, diag.Arg1, diag.Arg2, diag.Arg3));
                break;
            case DiagnosticIds.DbContextMissingSchemaWiring:
                spc.ReportDiagnostic(Diagnostic.Create(GeneratorConstants.DbContextSchemaNotWired, location, diag.Arg0, diag.Arg1));
                break;
        }
    }
}
