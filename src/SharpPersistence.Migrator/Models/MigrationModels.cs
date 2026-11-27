namespace SharpPersistence.Migrator.Models;

public sealed record AppliedMigrationRecord(
    Guid ScriptId,
    string ScriptName,
    string ChecksumSha256,
    DateTimeOffset AppliedAt);

public sealed record MigrationStatusItem(
    Guid ScriptId,
    string RegistryName,
    string ScriptName,
    string FullPath,
    bool IsApplied,
    bool HasChecksumMismatch,
    DateTimeOffset? AppliedAt);

internal sealed record GeneratedMigrationItem(
    string RegistryName,
    string? ScriptPath,
    string? DeferredScriptPath,
    string SqlScript,
    string DeferredSqlScript,
    bool IsEmpty,
    string? SnapshotPath = null,
    string? RenamedSnapshotFrom = null);

internal sealed record SchemaDriftItem(
    string RegistryName,
    string TableName,
    string? ColumnName,
    string Kind,
    string Details);
