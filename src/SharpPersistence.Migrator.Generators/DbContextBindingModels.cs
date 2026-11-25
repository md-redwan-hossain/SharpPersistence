using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpPersistence.Migrator.Generators;

internal readonly record struct SourceLocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public static SourceLocationInfo? From(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        var lineSpan = location.GetLineSpan();
        return new SourceLocationInfo(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
}

internal sealed record PendingDiagnostic(
    string DiagnosticId,
    SourceLocationInfo? Location,
    string Arg0,
    string Arg1 = "",
    string Arg2 = "",
    string Arg3 = "");

internal sealed record GeneratedDbContextProperty(
    string EntityFullTypeName,
    string PropertyName,
    bool IsReadOnly);

internal sealed record DbContextBindingModel(
    string HintName,
    string SimpleName,
    string? Namespace,
    EquatableArray<string> ContainingTypeDeclarations,
    EquatableArray<GeneratedDbContextProperty> Properties,
    EquatableArray<string> BoundEntityTypeNames,
    bool GenerateConstructor,
    bool GenerateOnModelCreating,
    string? ExistingSchemaMemberName,
    EquatableArray<PendingDiagnostic> Diagnostics);
