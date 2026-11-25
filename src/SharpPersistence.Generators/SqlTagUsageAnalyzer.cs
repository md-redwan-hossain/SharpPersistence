using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace SharpPersistence.Generators;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SqlTagUsageAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticCategory = "SharpPersistence.SqlParser";
    private const string ParsedSqlStorageMetadataName = "SharpPersistence.Abstractions.IParsedSqlStorage";
    private const string AssemblyMetadataAttributeName = "System.Reflection.AssemblyMetadataAttribute";
    private const string SqlTagMetadataKey = "SharpPersistence.SqlTag";

    public static readonly DiagnosticDescriptor UnknownSqlTag = new(
        id: "SPSQL006",
        title: "Unknown SQL tag",
        messageFormat: "SQL tag '{0}' does not match any known '-- #start#' tag in this project or its referenced assemblies",
        category: DiagnosticCategory,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [UnknownSqlTag];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            var compilation = compilationContext.Compilation;
            var storageInterface = compilation.GetTypeByMetadataName(ParsedSqlStorageMetadataName);
            if (storageInterface is null)
            {
                return;
            }

            var knownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasAnySqlSource = false;

            // 1. Local .sql AdditionalFiles
            foreach (var file in compilationContext.Options.AdditionalFiles)
            {
                if (!file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasAnySqlSource = true;
                var text = file.GetText(compilationContext.CancellationToken)?.ToString();
                foreach (var tag in SqlStorageExtensionGenerator.ExtractValidTags(text))
                {
                    knownTags.Add(tag);
                }
            }

            // 2. Cross-project tags exposed via [assembly: AssemblyMetadata("SharpPersistence.SqlTag", "...")]
            var asmMetaAttr = compilation.GetTypeByMetadataName(AssemblyMetadataAttributeName);
            if (asmMetaAttr is not null)
            {
                foreach (var reference in compilation.References)
                {
                    if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol asm)
                    {
                        continue;
                    }

                    foreach (var attr in asm.GetAttributes())
                    {
                        if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, asmMetaAttr) ||
                            attr.ConstructorArguments.Length != 2)
                        {
                            continue;
                        }

                        if (attr.ConstructorArguments[0].Value is string key &&
                            string.Equals(key, SqlTagMetadataKey, StringComparison.Ordinal) &&
                            attr.ConstructorArguments[1].Value is string tag &&
                            !string.IsNullOrWhiteSpace(tag))
                        {
                            hasAnySqlSource = true;
                            knownTags.Add(tag);
                        }
                    }
                }
            }

            // Skip projects with no .sql sources (e.g., ParseFromString-only unit test projects)
            if (!hasAnySqlSource)
            {
                return;
            }

            compilationContext.RegisterOperationAction(opCtx =>
            {
                if (opCtx.Operation is IPropertyReferenceOperation propRef)
                {
                    if (!propRef.Property.IsIndexer ||
                        propRef.Arguments.Length != 1 ||
                        !IsParsedSqlStorageType(propRef.Property.ContainingType, storageInterface))
                    {
                        return;
                    }

                    CheckTagArgument(opCtx, propRef.Arguments[0].Value, knownTags);
                }
                else if (opCtx.Operation is IInvocationOperation invocation)
                {
                    var method = invocation.TargetMethod;
                    if (method.Name != "TryGetParsedSql" ||
                        invocation.Arguments.Length < 1 ||
                        !IsParsedSqlStorageType(method.ContainingType, storageInterface))
                    {
                        return;
                    }

                    CheckTagArgument(opCtx, invocation.Arguments[0].Value, knownTags);
                }
            }, OperationKind.PropertyReference, OperationKind.Invocation);
        });
    }

    private static void CheckTagArgument(
        OperationAnalysisContext opCtx,
        IOperation argValue,
        HashSet<string> knownTags)
    {
        if (argValue.ConstantValue is not { HasValue: true, Value: string literalTag })
        {
            return;
        }

        if (!knownTags.Contains(literalTag))
        {
            opCtx.ReportDiagnostic(Diagnostic.Create(
                UnknownSqlTag,
                argValue.Syntax.GetLocation(),
                literalTag));
        }
    }

    private static bool IsParsedSqlStorageType(INamedTypeSymbol? type, INamedTypeSymbol storageInterface)
    {
        if (type is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(type, storageInterface))
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, storageInterface))
            {
                return true;
            }
        }

        return false;
    }
}
