namespace SharpPersistence.Migrator.Models;

public sealed record AppliedMigrationRecord(
    string RegistryName,
    string ScriptName,
    string ChecksumSha256,
    DateTimeOffset AppliedAt);

public sealed record MigrationStatusItem(
    string RegistryName,
    string ScriptName,
    string FullPath,
    bool IsApplied,
    bool HasChecksumMismatch,
    DateTimeOffset? AppliedAt);

public sealed record GeneratedMigrationItem(
    string RegistryName,
    string? ScriptPath,
    string? DeferredScriptPath,
    string SqlScript,
    string DeferredSqlScript,
    bool IsEmpty);

public sealed record SchemaDriftItem(
    string RegistryName,
    string TableName,
    string? ColumnName,
    string Kind,
    string Details);
