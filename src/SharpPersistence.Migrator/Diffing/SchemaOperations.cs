using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Diffing;

internal abstract record SchemaOperation;

internal sealed record EnsureExtensionOp(string Name, string? Schema) : SchemaOperation;

internal sealed record DropExtensionOp(string Name) : SchemaOperation;

internal sealed record CreateEnumOp(PostgresEnumSchema Enum) : SchemaOperation;

internal sealed record AddEnumValueOp(string? Schema, string EnumName, string Value) : SchemaOperation;

internal sealed record DropEnumOp(string? Schema, string Name) : SchemaOperation;

internal sealed record EnsureSchemaOp(string SchemaName) : SchemaOperation;

internal sealed record CreateSequenceOp(SequenceSchema Sequence) : SchemaOperation;

internal sealed record AlterSequenceOp(SequenceSchema OldSeq, SequenceSchema NewSeq) : SchemaOperation;

internal sealed record DropSequenceOp(string? Schema, string Name) : SchemaOperation;

internal sealed record CreateTableOp(TableSchema Table) : SchemaOperation;

internal sealed record DropTableOp(string? Schema, string TableName) : SchemaOperation;

internal sealed record RenameTableOp(string? OldSchema, string OldTableName, string? NewSchema, string NewTableName)
    : SchemaOperation;

internal sealed record AddColumnOp(string? Schema, string TableName, ColumnSchema Column) : SchemaOperation;

internal sealed record DropColumnOp(string? Schema, string TableName, string ColumnName) : SchemaOperation;

internal sealed record RenameColumnOp(string? Schema, string TableName, string OldName, string NewName) : SchemaOperation;

internal sealed record AlterColumnOp(string? Schema, string TableName, ColumnSchema OldCol, ColumnSchema NewCol)
    : SchemaOperation;

internal sealed record DropPrimaryKeyOp(string? Schema, string TableName, string Name) : SchemaOperation;

internal sealed record AddPrimaryKeyOp(string? Schema, string TableName, PrimaryKeySchema PrimaryKey) : SchemaOperation;

internal sealed record DropCheckConstraintOp(string? Schema, string TableName, string Name) : SchemaOperation;

internal sealed record AddCheckConstraintOp(string? Schema, string TableName, CheckConstraintSchema Constraint)
    : SchemaOperation;

internal sealed record DropExclusionConstraintOp(string? Schema, string TableName, string Name) : SchemaOperation;

internal sealed record AddExclusionConstraintOp(string? Schema, string TableName, ExclusionConstraintSchema Constraint)
    : SchemaOperation;

internal sealed record DropForeignKeyOp(string? Schema, string TableName, string Name) : SchemaOperation;

internal sealed record AddForeignKeyOp(string? Schema, string TableName, ForeignKeySchema ForeignKey) : SchemaOperation;

internal sealed record RenameConstraintOp(string? Schema, string TableName, string OldName, string NewName)
    : SchemaOperation;

internal sealed record DropIndexOp(string? Schema, string TableName, string Name) : SchemaOperation;

internal sealed record RenameIndexOp(string? Schema, string TableName, string OldName, string NewName) : SchemaOperation;

internal sealed record CreateIndexOp(string? Schema, string TableName, IndexSchema Index) : SchemaOperation;

internal sealed record SetTableCommentOp(string? Schema, string TableName, string? Comment) : SchemaOperation;

internal sealed record SetColumnCommentOp(string? Schema, string TableName, string ColumnName, string? Comment)
    : SchemaOperation;

internal sealed record SetTableLoggedOp(string? Schema, string TableName, bool IsUnlogged) : SchemaOperation;

internal sealed record SetTableTablespaceOp(string? Schema, string TableName, string Tablespace) : SchemaOperation;

internal sealed record AlterTableStorageParametersOp(
    string? Schema,
    string TableName,
    IReadOnlyDictionary<string, string> SetParameters,
    IReadOnlyList<string> ResetParameters) : SchemaOperation;

internal sealed record SetIndexTablespaceOp(string? Schema, string IndexName, string Tablespace) : SchemaOperation;

internal sealed record AlterIndexStorageParametersOp(
    string? Schema,
    string IndexName,
    IReadOnlyDictionary<string, string> SetParameters,
    IReadOnlyList<string> ResetParameters) : SchemaOperation;

internal sealed record SchemaDiffResult(
    bool HasChanges,
    string SqlScript,
    string DeferredSqlScript,
    string UpdatedSnapshotJson,
    IReadOnlyList<SchemaOperation>? Operations = null);
