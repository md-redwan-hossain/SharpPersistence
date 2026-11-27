namespace SharpPersistence.Migrator.Models;

internal readonly record struct QualifiedObjectName(string? Schema, string Name);

internal readonly record struct ForeignKeyReferenceRequirement(
    string? DependentSchema,
    string? PrincipalSchema,
    string PrincipalTable,
    string? ConstraintName);

internal sealed class DatabasePermissionRequirements
{
    public HashSet<string> SchemasToCreate { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SchemasTouched { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> TableCreateSchemas { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<QualifiedObjectName> TableAlters { get; } = [];
    public HashSet<QualifiedObjectName> TableDrops { get; } = [];

    public HashSet<string> SequenceCreateSchemas { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SequenceAlterSchemas { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> SequenceDropSchemas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<QualifiedObjectName> IndexCreateTables { get; } = [];
    public HashSet<string> IndexDropSchemas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> TypeSchemas { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<ForeignKeyReferenceRequirement> ForeignKeyReferences { get; } = [];
    public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Tablespaces { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? HistorySchema { get; init; }
    public string HistoryTableName { get; init; } = string.Empty;
    public bool RequiresHistoryTableCreation { get; init; }

    public bool HasAnyRequirements =>
        RequiresHistoryTableCreation ||
        SchemasToCreate.Count > 0 ||
        SchemasTouched.Count > 0 ||
        TableCreateSchemas.Count > 0 ||
        TableAlters.Count > 0 ||
        TableDrops.Count > 0 ||
        SequenceCreateSchemas.Count > 0 ||
        SequenceAlterSchemas.Count > 0 ||
        SequenceDropSchemas.Count > 0 ||
        IndexCreateTables.Count > 0 ||
        IndexDropSchemas.Count > 0 ||
        TypeSchemas.Count > 0 ||
        ForeignKeyReferences.Count > 0 ||
        Extensions.Count > 0 ||
        Tablespaces.Count > 0;
}
