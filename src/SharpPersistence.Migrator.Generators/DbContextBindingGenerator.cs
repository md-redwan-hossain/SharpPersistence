using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
                static (ctx, _) => DbContextBindingAnalyzer.AnalyzeBoundDbContext(ctx))
            .Where(static x => x is not null)
            .Select(static (x, _) => x!);

        var orphanItemDiagnostics = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                GeneratorConstants.EntityRegistryItemAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => DbContextBindingAnalyzer.AnalyzeOrphanItemClass(ctx))
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
