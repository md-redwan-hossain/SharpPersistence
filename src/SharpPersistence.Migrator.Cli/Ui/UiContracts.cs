namespace SharpPersistence.Migrator.Cli.Ui;

internal sealed record UiSnapshotDto(
    string ProjectName,
    string StartupProjectPath,
    string Dialect,
    string GeneratedAtUtc,
    UiModelDto Model,
    UiRunlistDto Runlist,
    UiDoctorDto Doctor);

internal sealed record UiModelDto(
    IReadOnlyList<UiRegistryDto> Registries,
    IReadOnlyList<UiPostgresEnumDto> PostgresEnums,
    IReadOnlyList<UiSequenceDto> Sequences);

internal sealed record UiRegistryDto(
    string Name,
    IReadOnlyList<UiTableDto> Tables);

internal sealed record UiTableDto(
    string RegistryName,
    string EntityName,
    string TableName,
    string? Schema,
    string QualifiedName,
    string? Comment,
    bool ExcludeFromMigrations,
    bool IsUnlogged,
    string? Tablespace,
    IReadOnlyDictionary<string, string>? StorageParameters,
    UiPrimaryKeyDto? PrimaryKey,
    IReadOnlyList<UiColumnDto> Columns,
    IReadOnlyList<UiForeignKeyDto> ForeignKeys,
    IReadOnlyList<UiIndexDto> Indexes,
    IReadOnlyList<UiCheckConstraintDto> CheckConstraints,
    IReadOnlyList<UiExclusionConstraintDto> ExclusionConstraints,
    string DdlPreview);

internal sealed record UiPrimaryKeyDto(
    string Name,
    IReadOnlyList<string> Columns);

internal sealed record UiColumnDto(
    string ColumnName,
    string PropertyName,
    string ClrTypeName,
    string SqlType,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsForeignKey,
    bool IsIdentity,
    bool IsOwnedColumn,
    bool IsConcurrencyToken,
    bool IsRowVersion,
    bool IsSystemColumn,
    int? MaxLength,
    int? Precision,
    int? Scale,
    string? DefaultValueSql,
    string? ComputedColumnSql,
    bool IsComputedStored,
    string? Comment);

internal sealed record UiForeignKeyDto(
    string Name,
    IReadOnlyList<string> DependentColumns,
    string PrincipalEntityName,
    string? PrincipalSchema,
    string PrincipalTable,
    string PrincipalQualifiedName,
    IReadOnlyList<string> PrincipalColumns,
    string OnDelete,
    bool IsCircular);

internal sealed record UiIndexDto(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<bool>? IsDescending,
    bool IsUnique,
    string? FilterSql,
    IReadOnlyList<string>? IncludedColumns,
    string? Method,
    string? Tablespace,
    IReadOnlyDictionary<string, string>? StorageParameters);

internal sealed record UiCheckConstraintDto(
    string Name,
    string Sql);

internal sealed record UiExclusionConstraintDto(
    string Name,
    string Method,
    IReadOnlyList<string> Elements,
    string? FilterSql);

internal sealed record UiPostgresEnumDto(
    string Name,
    string? Schema,
    IReadOnlyList<string> Labels);

internal sealed record UiSequenceDto(
    string Name,
    string? Schema,
    string ClrTypeName,
    long StartValue,
    int IncrementBy,
    long? MinValue,
    long? MaxValue,
    bool IsCyclic);

internal sealed record UiRunlistDto(
    string? Error,
    IReadOnlyList<UiRunlistScriptDto> Scripts);

internal sealed record UiRunlistScriptDto(
    int Order,
    string Id,
    string RegistryName,
    string ScriptName,
    bool IsOverridden,
    string? Sql);

internal sealed record UiDoctorDto(
    bool CheckedDatabase,
    int PassedCount,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<UiDoctorItemDto> WiringChecks,
    IReadOnlyList<UiDoctorRegistryDto> RegistryReports,
    IReadOnlyList<UiDoctorItemDto> CrossRegistryChecks,
    IReadOnlyList<UiDoctorItemDto> MigrationCoverageChecks,
    IReadOnlyList<UiDoctorItemDto> DatabaseChecks);

internal sealed record UiDoctorRegistryDto(
    string RegistryName,
    int EntityCount,
    int ExcludedEntityCount,
    int ColumnCount,
    int ForeignKeyCount,
    int ConstraintCount,
    IReadOnlyList<UiDoctorItemDto> Items);

internal sealed record UiDoctorItemDto(
    string Severity,
    string Message,
    string? Hint,
    string? EntityName);
