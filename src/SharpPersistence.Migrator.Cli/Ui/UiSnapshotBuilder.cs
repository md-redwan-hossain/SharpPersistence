using System.Text.Json;
using SharpPersistence.Migrator.Diffing;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Cli.Ui;

internal static class UiSnapshotBuilder
{
    public const string SnapshotPlaceholder = "window.__SHARP_PERSISTENCE_SNAPSHOT__ = null;";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static async Task<UiSnapshotDto> BuildSnapshotAsync(
        LoadedMigrationContext loaded,
        bool checkDatabase,
        bool includeScriptSql,
        CancellationToken ct)
    {
        var model = BuildModel(loaded.Manager);
        var runlist = BuildRunlist(loaded.Manager, includeScriptSql);
        var doctor = await BuildDoctorAsync(loaded, checkDatabase, ct);

        return new UiSnapshotDto(
            ProjectName: loaded.HostName,
            StartupProjectPath: loaded.StartupProjectDisplayPath,
            Dialect: loaded.Manager.Options.Dialect.ToString(),
            GeneratedAtUtc: DateTimeOffset.UtcNow.ToString("O"),
            Model: model,
            Runlist: runlist,
            Doctor: doctor);
    }

    public static UiModelDto BuildModel(MigrationManager manager)
    {
        var options = manager.Options;
        var sortedRegistries = RegistryDependencyResolver.SortTopologically(options);
        var registryDtos = new List<UiRegistryDto>(sortedRegistries.Count);
        var sequences = new List<UiSequenceDto>();
        var seenSequences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registry in sortedRegistries)
        {
            var registryName = registry.GetType().Name;
            var regModel = SchemaScanner.ScanRegistry(registry, options, sortedRegistries);

            if (regModel.Sequences is { Count: > 0 })
            {
                foreach (var seq in regModel.Sequences)
                {
                    var key = $"{seq.Schema ?? string.Empty}.{seq.Name}";
                    if (seenSequences.Add(key))
                    {
                        sequences.Add(new UiSequenceDto(
                            seq.Name,
                            seq.Schema,
                            seq.ClrTypeName,
                            seq.StartValue,
                            seq.IncrementBy,
                            seq.MinValue,
                            seq.MaxValue,
                            seq.IsCyclic));
                    }
                }
            }

            var tableDtos = new List<UiTableDto>(regModel.Tables.Count);
            foreach (var table in regModel.Tables.OrderBy(static t => t.EntityName, StringComparer.Ordinal))
            {
                tableDtos.Add(MapTable(registryName, table, options));
            }

            registryDtos.Add(new UiRegistryDto(registryName, tableDtos));
        }

        var enums = options.Enums
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .Select(static e => new UiPostgresEnumDto(e.Name, e.Schema, [.. e.Labels]))
            .ToList();

        return new UiModelDto(
            registryDtos,
            enums,
            sequences.OrderBy(static s => s.Name, StringComparer.Ordinal).ToList());
    }

    public static UiRunlistDto BuildRunlist(MigrationManager manager, bool includeSql)
    {
        try
        {
            var runlist = manager.CreateRunlistManager();
            var entries = runlist.ResolveEffectiveOrder();
            var scripts = new List<UiRunlistScriptDto>(entries.Count);

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var sql = includeSql && File.Exists(e.FullPath) ? File.ReadAllText(e.FullPath) : null;
                scripts.Add(new UiRunlistScriptDto(
                    Order: i + 1,
                    Id: e.Id.ToString("D"),
                    RegistryName: e.RegistryName,
                    ScriptName: e.ScriptName,
                    IsOverridden: e.IsOverridden,
                    Sql: sql));
            }

            return new UiRunlistDto(Error: null, scripts);
        }
        catch (Exception ex)
        {
            return new UiRunlistDto(Error: ex.Message, []);
        }
    }

    public static string? ReadRunlistScriptSql(MigrationManager manager, string scriptId)
    {
        if (!Guid.TryParse(scriptId, out var targetId))
        {
            return null;
        }

        var entries = manager.CreateRunlistManager().ResolveEffectiveOrder();
        var match = entries.FirstOrDefault(e => e.Id == targetId);
        if (match is null || !File.Exists(match.FullPath))
        {
            return null;
        }

        return File.ReadAllText(match.FullPath);
    }

    public static async Task<UiDoctorDto> BuildDoctorAsync(
        LoadedMigrationContext loaded,
        bool checkDatabase,
        CancellationToken ct)
    {
        var report = await loaded.Manager.DiagnoseAsync(
            registryFilter: null,
            checkDatabase: checkDatabase,
            discoveredAssemblies: loaded.LoadedAssemblies,
            ct: ct);

        return new UiDoctorDto(
            CheckedDatabase: report.CheckedDatabase,
            PassedCount: report.PassedCount,
            WarningCount: report.WarningCount,
            ErrorCount: report.ErrorCount,
            WiringChecks: MapDoctorItems(report.WiringChecks),
            RegistryReports: report.RegistryReports
                .Select(static r => new UiDoctorRegistryDto(
                    r.RegistryName,
                    r.EntityCount,
                    r.ExcludedEntityCount,
                    r.ColumnCount,
                    r.ForeignKeyCount,
                    r.ConstraintCount,
                    MapDoctorItems(r.Items)))
                .ToList(),
            CrossRegistryChecks: MapDoctorItems(report.CrossRegistryChecks),
            MigrationCoverageChecks: MapDoctorItems(report.MigrationCoverageChecks),
            DatabaseChecks: MapDoctorItems(report.DatabaseChecks));
    }

    public static string InjectSnapshotIntoHtml(string htmlTemplate, UiSnapshotDto snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions)
            .Replace("</", "<\\/", StringComparison.Ordinal);
        var replacement = $"window.__SHARP_PERSISTENCE_SNAPSHOT__ = {json};";

        if (htmlTemplate.Contains(SnapshotPlaceholder, StringComparison.Ordinal))
        {
            return htmlTemplate.Replace(SnapshotPlaceholder, replacement, StringComparison.Ordinal);
        }

        var scriptTag = $"<script>{replacement}</script>";
        var headCloseIdx = htmlTemplate.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return headCloseIdx >= 0
            ? htmlTemplate.Insert(headCloseIdx, scriptTag)
            : scriptTag + htmlTemplate;
    }

    private static UiTableDto MapTable(
        string registryName,
        TableSchema table,
        Configuration.MigrationManagerOptions options)
    {
        var pkCols = new HashSet<string>(
            table.PrimaryKey?.ColumnNames ?? [],
            StringComparer.OrdinalIgnoreCase);

        var fkCols = new HashSet<string>(
            table.ForeignKeys.SelectMany(static fk => fk.DependentColumns),
            StringComparer.OrdinalIgnoreCase);

        var columns = table.Columns
            .Select(c => new UiColumnDto(
                ColumnName: c.ColumnName,
                PropertyName: c.PropertyName,
                ClrTypeName: c.ClrTypeName,
                SqlType: c.SqlType,
                IsNullable: c.IsNullable,
                IsPrimaryKey: pkCols.Contains(c.ColumnName),
                IsForeignKey: fkCols.Contains(c.ColumnName),
                IsIdentity: c.IsIdentity,
                IsOwnedColumn: c.IsOwnedColumn,
                IsConcurrencyToken: c.IsConcurrencyToken,
                IsRowVersion: c.IsRowVersion,
                IsSystemColumn: c.IsSystemColumn,
                MaxLength: c.MaxLength,
                Precision: c.Precision,
                Scale: c.Scale,
                DefaultValueSql: c.DefaultValueSql,
                ComputedColumnSql: c.ComputedColumnSql,
                IsComputedStored: c.IsComputedStored,
                Comment: c.Comment))
            .ToList();

        var fks = table.ForeignKeys
            .Select(static fk => new UiForeignKeyDto(
                Name: fk.Name,
                DependentColumns: [.. fk.DependentColumns],
                PrincipalEntityName: fk.PrincipalEntityName,
                PrincipalSchema: fk.PrincipalSchema,
                PrincipalTable: fk.PrincipalTable,
                PrincipalQualifiedName: string.IsNullOrWhiteSpace(fk.PrincipalSchema)
                    ? fk.PrincipalTable
                    : $"{fk.PrincipalSchema}.{fk.PrincipalTable}",
                PrincipalColumns: [.. fk.PrincipalColumns],
                OnDelete: fk.OnDelete.ToString(),
                IsCircular: fk.IsCircular))
            .ToList();

        var indexes = table.Indexes
            .Select(static ix => new UiIndexDto(
                Name: ix.Name,
                Columns: [.. ix.ColumnNames],
                IsDescending: ix.IsDescending is null ? null : [.. ix.IsDescending],
                IsUnique: ix.IsUnique,
                FilterSql: ix.FilterSql,
                IncludedColumns: ix.IncludedColumnNames is null ? null : [.. ix.IncludedColumnNames],
                Method: ix.Method,
                Tablespace: ix.Tablespace,
                StorageParameters: ix.StorageParameters))
            .ToList();

        var checks = table.CheckConstraints
            .Select(static ck => new UiCheckConstraintDto(ck.Name, ck.Sql))
            .ToList();

        var exclusions = (table.ExclusionConstraints ?? [])
            .Select(static ex => new UiExclusionConstraintDto(
                ex.Name,
                ex.Method,
                ex.Elements.Select(static el =>
                {
                    var expr = !string.IsNullOrWhiteSpace(el.ExpressionSql) ? el.ExpressionSql : el.ColumnName ?? el.PropertyName ?? "?";
                    var opClass = string.IsNullOrWhiteSpace(el.OperatorClass) ? string.Empty : $" {el.OperatorClass}";
                    return $"{expr}{opClass} WITH {el.Operator}";
                }).ToList(),
                ex.FilterSql))
            .ToList();

        var ddlPreview = GenerateTableDdlPreview(table, options);

        return new UiTableDto(
            RegistryName: registryName,
            EntityName: table.EntityName,
            TableName: table.TableName,
            Schema: table.Schema,
            QualifiedName: table.QualifiedName,
            Comment: table.Comment,
            ExcludeFromMigrations: table.ExcludeFromMigrations,
            IsUnlogged: table.IsUnlogged,
            Tablespace: table.Tablespace,
            StorageParameters: table.StorageParameters,
            PrimaryKey: table.PrimaryKey is null
                ? null
                : new UiPrimaryKeyDto(table.PrimaryKey.Name, [.. table.PrimaryKey.ColumnNames]),
            Columns: columns,
            ForeignKeys: fks,
            Indexes: indexes,
            CheckConstraints: checks,
            ExclusionConstraints: exclusions,
            DdlPreview: ddlPreview);
    }

    private static string GenerateTableDdlPreview(
        TableSchema table,
        Configuration.MigrationManagerOptions options)
    {
        try
        {
            var singleModel = new DatabaseModel
            {
                Tables = [table]
            };

            var diff = StandaloneSchemaDiffer.GenerateDiff(
                singleModel,
                null,
                options.Dialect,
                interactiveRenamePrompt: false,
                options.OracleCompatibility);

            return diff.SqlScript;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<UiDoctorItemDto> MapDoctorItems(IReadOnlyList<DoctorCheckItem> items) =>
        items.Select(static i => new UiDoctorItemDto(
            Severity: i.Severity.ToString(),
            Message: i.Message,
            Hint: i.Hint,
            EntityName: i.EntityName)).ToList();
}
