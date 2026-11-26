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

internal readonly record struct GeneratedDbContextProperty
{
    public required string EntityFullTypeName { get; init; }
    public required string PropertyName { get; init; }
    public required bool IsReadOnly { get; init; }
}

internal sealed record DbContextBindingModel
{
    public required string HintName { get; init; }
    public required string SimpleName { get; init; }
    public required string? Namespace { get; init; }
    public required EquatableArray<string> ContainingTypeDeclarations { get; init; }
    public required EquatableArray<GeneratedDbContextProperty> Properties { get; init; }
    public required EquatableArray<string> BoundEntityTypeNames { get; init; }
    public required bool GenerateConstructor { get; init; }
    public required bool GenerateOnModelCreating { get; init; }
    public required string? ExistingSchemaMemberName { get; init; }
    public required EquatableArray<PendingDiagnostic> Diagnostics { get; init; }
}
