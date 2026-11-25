using System.Collections.Concurrent;
using System.Text.Json;
using SharpPersistence.Migrator.Models;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Cli.Ui;

internal sealed class UiDataService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly LoadedMigrationContext _loaded;
    private readonly IReadOnlyList<(string RegistryName, DatabaseModel Model)> _scannedRegistries;
    private readonly Dictionary<(string Registry, string Entity), TableSchema> _tableLookup;
    private readonly ConcurrentDictionary<(string Registry, string Entity), UiTableDetailDto> _detailCache = new();

    public UiDataService(LoadedMigrationContext loaded)
    {
        _loaded = loaded;
        var options = loaded.Manager.Options;
        var sortedRegistries = RegistryDependencyResolver.SortTopologically(options);

        var scanned = new List<(string, DatabaseModel)>(sortedRegistries.Count);
        var lookup = new Dictionary<(string, string), TableSchema>(TupleKeyComparer.Instance);

        foreach (var registry in sortedRegistries)
        {
            var registryName = registry.GetType().Name;
            var model = SchemaScanner.ScanRegistry(registry, options, sortedRegistries);
            scanned.Add((registryName, model));

            foreach (var table in model.Tables)
            {
                lookup[(registryName, table.EntityName)] = table;
            }
        }

        _scannedRegistries = scanned;
        _tableLookup = lookup;
    }

    public UiOverviewDto BuildOverview()
    {
        var options = _loaded.Manager.Options;
        var registrySummaries = new List<UiRegistrySummaryDto>(_scannedRegistries.Count);
        var sequences = new List<UiSequenceDto>();
        var seenSequences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalEntities = 0;

        foreach (var (registryName, model) in _scannedRegistries)
        {
            if (model.Sequences is { Count: > 0 })
            {
                foreach (var seq in model.Sequences)
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

            var tableSummaries = model.Tables
                .OrderBy(static t => GetShortName(t.EntityName), StringComparer.Ordinal)
                .Select(t => new UiTableSummaryDto(
                    RegistryName: registryName,
                    EntityName: t.EntityName,
                    ShortEntityName: GetShortName(t.EntityName),
                    TableName: t.TableName,
                    Schema: t.Schema,
                    QualifiedName: t.QualifiedName,
                    ExcludeFromMigrations: t.ExcludeFromMigrations,
                    ColumnCount: t.Columns.Count,
                    ForeignKeyCount: t.ForeignKeys.Count,
                    IndexCount: t.Indexes.Count))
                .ToList();

            totalEntities += tableSummaries.Count;
            registrySummaries.Add(new UiRegistrySummaryDto(registryName, tableSummaries));
        }

        var enums = options.Enums
            .OrderBy(static e => e.Name, StringComparer.Ordinal)
            .Select(static e => new UiPostgresEnumDto(e.Name, e.Schema, [.. e.Labels]))
            .ToList();

        var runlist = BuildRunlist();

        return new UiOverviewDto(
            ProjectName: _loaded.HostName,
            StartupProjectPath: _loaded.StartupProjectDisplayPath,
            Dialect: options.Dialect.ToString(),
            TotalEntities: totalEntities,
            RunlistScriptCount: runlist.Scripts.Count,
            RunlistError: runlist.Error,
            Registries: registrySummaries,
            PostgresEnums: enums,
            Sequences: sequences.OrderBy(static s => s.Name, StringComparer.Ordinal).ToList());
    }

    public UiTableDetailDto? GetTableDetail(string registryName, string entityName)
    {
        if (!_tableLookup.TryGetValue((registryName, entityName), out var table))
        {
            return null;
        }

        return _detailCache.GetOrAdd(
            (registryName, entityName),
            _ => MapTableDetail(registryName, table));
    }

    public UiErdDto BuildErd(string? registryFilter)
    {
        var selectedRegistries = string.IsNullOrWhiteSpace(registryFilter) ||
                                 string.Equals(registryFilter, "ALL", StringComparison.OrdinalIgnoreCase)
            ? _scannedRegistries
            : _scannedRegistries
                .Where(r => string.Equals(r.RegistryName, registryFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var nodes = new List<UiErdNodeDto>();
        var nodeKeyByTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var nodeKeyByEntity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (registryName, model) in selectedRegistries)
        {
            foreach (var t in model.Tables.OrderBy(static x => GetShortName(x.EntityName), StringComparer.Ordinal))
            {
                var key = $"{registryName}::{t.EntityName}";
                var pkCols = new HashSet<string>(t.PrimaryKey?.ColumnNames ?? [], StringComparer.OrdinalIgnoreCase);
                var fkCols = new HashSet<string>(t.ForeignKeys.SelectMany(static fk => fk.DependentColumns), StringComparer.OrdinalIgnoreCase);

                var cols = t.Columns
                    .Take(12)
                    .Select(c => new UiErdColumnDto(
                        c.ColumnName,
                        c.SqlType,
                        pkCols.Contains(c.ColumnName),
                        fkCols.Contains(c.ColumnName),
                        c.IsNullable))
                    .ToList();

                nodes.Add(new UiErdNodeDto(
                    Key: key,
                    RegistryName: registryName,
                    EntityName: t.EntityName,
                    ShortEntityName: GetShortName(t.EntityName),
                    QualifiedName: t.QualifiedName,
                    ExcludeFromMigrations: t.ExcludeFromMigrations,
                    TotalColumns: t.Columns.Count,
                    Columns: cols));

                nodeKeyByTable[t.QualifiedName] = key;
                nodeKeyByEntity[t.EntityName] = key;
            }
        }

        var edges = new List<UiErdEdgeDto>();
        foreach (var (registryName, model) in selectedRegistries)
        {
            foreach (var t in model.Tables)
            {
                var fromKey = $"{registryName}::{t.EntityName}";
                foreach (var fk in t.ForeignKeys)
                {
                    var principalQualified = string.IsNullOrWhiteSpace(fk.PrincipalSchema)
                        ? fk.PrincipalTable
                        : $"{fk.PrincipalSchema}.{fk.PrincipalTable}";

                    if (!nodeKeyByTable.TryGetValue(principalQualified, out var toKey) &&
                        !nodeKeyByEntity.TryGetValue(fk.PrincipalEntityName, out toKey))
                    {
                        continue;
                    }

                    if (string.Equals(fromKey, toKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    edges.Add(new UiErdEdgeDto(
                        Id: $"{fromKey}->{toKey}::{fk.Name}",
                        FromKey: fromKey,
                        ToKey: toKey,
                        Name: fk.Name,
                        OnDelete: fk.OnDelete.ToString()));
                }
            }
        }

        return new UiErdDto(registryFilter, nodes, edges);
    }

    public UiRunlistDto BuildRunlist()
    {
        try
        {
            var entries = _loaded.Manager.CreateRunlistManager().ResolveEffectiveOrder();
            var scripts = new List<UiRunlistScriptDto>(entries.Count);

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                scripts.Add(new UiRunlistScriptDto(
                    Order: i + 1,
                    Id: e.Id.ToString("D"),
                    RegistryName: e.RegistryName,
                    ScriptName: e.ScriptName,
                    IsOverridden: e.IsOverridden));
            }

            return new UiRunlistDto(Error: null, scripts);
        }
        catch (Exception ex)
        {
            return new UiRunlistDto(Error: ex.Message, []);
        }
    }

    public string? ReadRunlistScriptSql(string scriptId)
    {
        if (!Guid.TryParse(scriptId, out var targetId))
        {
            return null;
        }

        var entries = _loaded.Manager.CreateRunlistManager().ResolveEffectiveOrder();
        var match = entries.FirstOrDefault(e => e.Id == targetId);
        return match is not null && File.Exists(match.FullPath)
            ? File.ReadAllText(match.FullPath)
            : null;
    }

    public async Task<UiDoctorDto> BuildDoctorAsync(bool checkDatabase, CancellationToken ct)
    {
        var report = await _loaded.Manager.DiagnoseAsync(
            registryFilter: null,
            checkDatabase: checkDatabase,
            discoveredAssemblies: _loaded.LoadedAssemblies,
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

    private static UiTableDetailDto MapTableDetail(
        string registryName,
        TableSchema table)
    {
        var pkCols = new HashSet<string>(table.PrimaryKey?.ColumnNames ?? [], StringComparer.OrdinalIgnoreCase);
        var fkCols = new HashSet<string>(table.ForeignKeys.SelectMany(static fk => fk.DependentColumns), StringComparer.OrdinalIgnoreCase);

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

        return new UiTableDetailDto(
            RegistryName: registryName,
            EntityName: table.EntityName,
            ShortEntityName: GetShortName(table.EntityName),
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
            ExclusionConstraints: exclusions);
    }

    private static string GetShortName(string fullEntityName)
    {
        var plusIdx = fullEntityName.LastIndexOf('+');
        if (plusIdx >= 0 && plusIdx + 1 < fullEntityName.Length)
        {
            return fullEntityName[(plusIdx + 1)..];
        }

        var dotIdx = fullEntityName.LastIndexOf('.');
        return dotIdx >= 0 && dotIdx + 1 < fullEntityName.Length
            ? fullEntityName[(dotIdx + 1)..]
            : fullEntityName;
    }

    private static List<UiDoctorItemDto> MapDoctorItems(IReadOnlyList<DoctorCheckItem> items) =>
        items.Select(static i => new UiDoctorItemDto(
            Severity: i.Severity.ToString(),
            Message: i.Message,
            Hint: i.Hint,
            EntityName: i.EntityName)).ToList();

    private sealed class TupleKeyComparer : IEqualityComparer<(string Registry, string Entity)>
    {
        public static readonly TupleKeyComparer Instance = new();

        public bool Equals((string Registry, string Entity) x, (string Registry, string Entity) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Registry, y.Registry) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Entity, y.Entity);

        public int GetHashCode((string Registry, string Entity) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Registry),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Entity));
    }
}
