using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Diffing;

public abstract record SchemaOperation;

public sealed record EnsureExtensionOp(string Name, string? Schema) : SchemaOperation;

public sealed record DropExtensionOp(string Name) : SchemaOperation;

public sealed record CreateEnumOp(PostgresEnumSchema Enum) : SchemaOperation;

public sealed record AddEnumValueOp(string? Schema, string EnumName, string Value) : SchemaOperation;

public sealed record DropEnumOp(string? Schema, string Name) : SchemaOperation;

public sealed record EnsureSchemaOp(string SchemaName) : SchemaOperation;

public sealed record CreateSequenceOp(SequenceSchema Sequence) : SchemaOperation;

public sealed record AlterSequenceOp(SequenceSchema OldSeq, SequenceSchema NewSeq) : SchemaOperation;

public sealed record DropSequenceOp(string? Schema, string Name) : SchemaOperation;

public sealed record CreateTableOp(TableSchema Table) : SchemaOperation;

public sealed record DropTableOp(string? Schema, string TableName) : SchemaOperation;

public sealed record RenameTableOp(string? OldSchema, string OldTableName, string? NewSchema, string NewTableName)
    : SchemaOperation;

public sealed record AddColumnOp(string? Schema, string TableName, ColumnSchema Column) : SchemaOperation;

public sealed record DropColumnOp(string? Schema, string TableName, string ColumnName) : SchemaOperation;

public sealed record RenameColumnOp(string? Schema, string TableName, string OldName, string NewName) : SchemaOperation;

public sealed record AlterColumnOp(string? Schema, string TableName, ColumnSchema OldCol, ColumnSchema NewCol)
    : SchemaOperation;

public sealed record DropPrimaryKeyOp(string? Schema, string TableName, string Name) : SchemaOperation;

public sealed record AddPrimaryKeyOp(string? Schema, string TableName, PrimaryKeySchema PrimaryKey) : SchemaOperation;

public sealed record DropCheckConstraintOp(string? Schema, string TableName, string Name) : SchemaOperation;

public sealed record AddCheckConstraintOp(string? Schema, string TableName, CheckConstraintSchema Constraint)
    : SchemaOperation;

public sealed record DropExclusionConstraintOp(string? Schema, string TableName, string Name) : SchemaOperation;

public sealed record AddExclusionConstraintOp(string? Schema, string TableName, ExclusionConstraintSchema Constraint)
    : SchemaOperation;

public sealed record DropForeignKeyOp(string? Schema, string TableName, string Name) : SchemaOperation;

public sealed record AddForeignKeyOp(string? Schema, string TableName, ForeignKeySchema ForeignKey) : SchemaOperation;

public sealed record RenameConstraintOp(string? Schema, string TableName, string OldName, string NewName)
    : SchemaOperation;

public sealed record DropIndexOp(string? Schema, string TableName, string Name) : SchemaOperation;

public sealed record RenameIndexOp(string? Schema, string TableName, string OldName, string NewName) : SchemaOperation;

public sealed record CreateIndexOp(string? Schema, string TableName, IndexSchema Index) : SchemaOperation;

public sealed record SetTableCommentOp(string? Schema, string TableName, string? Comment) : SchemaOperation;

public sealed record SetColumnCommentOp(string? Schema, string TableName, string ColumnName, string? Comment)
    : SchemaOperation;

public sealed record SetTableLoggedOp(string? Schema, string TableName, bool IsUnlogged) : SchemaOperation;

public sealed record SetTableTablespaceOp(string? Schema, string TableName, string Tablespace) : SchemaOperation;

public sealed record AlterTableStorageParametersOp(
    string? Schema,
    string TableName,
    IReadOnlyDictionary<string, string> SetParameters,
    IReadOnlyList<string> ResetParameters) : SchemaOperation;

public sealed record SetIndexTablespaceOp(string? Schema, string IndexName, string Tablespace) : SchemaOperation;

public sealed record AlterIndexStorageParametersOp(
    string? Schema,
    string IndexName,
    IReadOnlyDictionary<string, string> SetParameters,
    IReadOnlyList<string> ResetParameters) : SchemaOperation;

public sealed record SchemaDiffResult(
    bool HasChanges,
    string SqlScript,
    string DeferredSqlScript,
    string UpdatedSnapshotJson);
