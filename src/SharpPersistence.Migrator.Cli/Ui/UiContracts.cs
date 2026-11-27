namespace SharpPersistence.Migrator.Cli.Ui;

internal sealed record UiOverviewDto(
    string ProjectName,
    string StartupProjectPath,
    string Dialect,
    int TotalEntities,
    int RunlistScriptCount,
    string? RunlistError,
    IReadOnlyList<UiRegistrySummaryDto> Registries,
    IReadOnlyList<UiPostgresEnumDto> PostgresEnums,
    IReadOnlyList<UiSequenceDto> Sequences);

internal sealed record UiRegistrySummaryDto(
    string Name,
    IReadOnlyList<UiTableSummaryDto> Tables);

internal sealed record UiTableSummaryDto(
    string RegistryName,
    string EntityName,
    string ShortEntityName,
    string TableName,
    string? Schema,
    string QualifiedName,
    bool ExcludeFromMigrations,
    int ColumnCount,
    int ForeignKeyCount,
    int IndexCount);

internal sealed record UiTableDetailDto(
    string RegistryName,
    string EntityName,
    string ShortEntityName,
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
    IReadOnlyList<UiExclusionConstraintDto> ExclusionConstraints);

internal sealed record UiErdDto(
    string? RegistryFilter,
    IReadOnlyList<UiErdNodeDto> Nodes,
    IReadOnlyList<UiErdEdgeDto> Edges);

internal sealed record UiErdNodeDto(
    string Key,
    string RegistryName,
    string EntityName,
    string ShortEntityName,
    string QualifiedName,
    bool ExcludeFromMigrations,
    int TotalColumns,
    IReadOnlyList<UiErdColumnDto> Columns);

internal sealed record UiErdColumnDto(
    string ColumnName,
    string SqlType,
    bool IsPrimaryKey,
    bool IsForeignKey,
    bool IsNullable);

internal sealed record UiErdEdgeDto(
    string Id,
    string FromKey,
    string ToKey,
    string Name,
    string OnDelete);

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
    IReadOnlyList<string> DependentProperties,
    string PrincipalEntityName,
    string? PrincipalSchema,
    string PrincipalTable,
    string PrincipalQualifiedName,
    IReadOnlyList<string> PrincipalColumns,
    IReadOnlyList<string> PrincipalProperties,
    string OnDelete,
    bool IsCircular);

internal sealed record UiIndexDto(
    string Name,
    IReadOnlyList<string> Columns,
    IReadOnlyList<bool>? IsDescending,
    bool IsUnique,
    string? FilterSql,
    IReadOnlyList<string>? IncludedColumns,
    IReadOnlyList<string>? IncludedProperties,
    IReadOnlyDictionary<string, string>? OperatorsByColumn,
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
    bool IsOverridden);

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

internal sealed record UiDependenciesDto(
    IReadOnlyList<UiDependencyNodeDto> Nodes,
    IReadOnlyList<UiDependencyEdgeDto> Edges,
    IReadOnlyList<UiEntityTypeReplacementDto> EntityTypeReplacements);

internal sealed record UiDependencyNodeDto(
    int Order,
    string RegistryName,
    int EntityCount,
    int ExcludedCount,
    IReadOnlyList<string> Schemas,
    int IncomingCount,
    int OutgoingCount);

internal sealed record UiDependencyEdgeDto(
    string Id,
    string FromRegistry,
    string ToRegistry,
    IReadOnlyList<UiDependencyFkDto> ForeignKeys);

internal sealed record UiDependencyFkDto(
    string ConstraintName,
    string FromEntity,
    string FromTable,
    IReadOnlyList<string> FromColumns,
    string ToEntity,
    string ToTable,
    IReadOnlyList<string> ToColumns,
    string OnDelete,
    bool IsCircular);

internal sealed record UiEntityTypeReplacementDto(
    string SourceEntity,
    string ShortSourceEntity,
    string TargetEntity,
    string ShortTargetEntity,
    string? TargetRegistry,
    string? TargetTable);

internal sealed record UiDryRunDto(
    int ChangedRegistryCount,
    int AddCount,
    int AlterCount,
    int DropCount,
    IReadOnlyList<UiDryRunRegistryDto> Registries);

internal sealed record UiDryRunRegistryDto(
    string RegistryName,
    bool HasChanges,
    bool HasDestructiveChanges,
    int AddCount,
    int AlterCount,
    int DropCount,
    IReadOnlyList<UiDryRunOperationDto> Operations,
    string SqlScript,
    string DeferredSqlScript);

internal sealed record UiDryRunOperationDto(
    string Kind,
    string ObjectType,
    string Target,
    string Summary);

internal sealed record UiSearchResponseDto(
    string Query,
    IReadOnlyList<UiSearchHitDto> Hits);

internal sealed record UiSearchHitDto(
    string Kind,
    string Name,
    string Detail,
    string RegistryName,
    string EntityName,
    string ShortEntityName,
    string QualifiedName);


internal sealed record UiDdlStatementDto(
    int Index,
    string Action,
    string ObjectType,
    string? Schema,
    string? TableName,
    string ObjectName,
    string Target,
    string Sql);

internal sealed record UiScriptDdlsDto(
    string ScriptId,
    IReadOnlyList<UiDdlStatementDto> Statements);

internal sealed record UiDdlCheckRequestDto(
    string Action,
    string ObjectType,
    string? Schema,
    string? TableName,
    string ObjectName,
    string Target);

internal sealed record UiDdlCheckResultDto(
    int? Index,
    string Target,
    bool Passed,
    bool ExistsInDb,
    string Detail,
    string? Error);

internal sealed record UiScriptLiveCheckDto(
    string ScriptId,
    bool Connected,
    bool? IsAppliedInHistory,
    long ElapsedMs,
    string? Error,
    IReadOnlyList<UiDdlCheckResultDto> Results);
