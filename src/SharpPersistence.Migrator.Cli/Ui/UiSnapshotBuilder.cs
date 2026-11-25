using System.Collections.Concurrent;
using System.Text.Json;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Diffing;
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
    private UiDependenciesDto? _dependenciesCache;

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

    public UiDependenciesDto BuildDependencies()
    {
        if (_dependenciesCache is not null)
        {
            return _dependenciesCache;
        }

        var options = _loaded.Manager.Options;
        var ownerByTable = new Dictionary<string, (string Registry, TableSchema Table)>(StringComparer.OrdinalIgnoreCase);
        var ownerByEntity = new Dictionary<string, (string Registry, TableSchema Table)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (registryName, model) in _scannedRegistries)
        {
            foreach (var t in model.Tables)
            {
                if (!t.ExcludeFromMigrations || !ownerByTable.ContainsKey(t.QualifiedName))
                {
                    ownerByTable[t.QualifiedName] = (registryName, t);
                }

                if (!t.ExcludeFromMigrations || !ownerByEntity.ContainsKey(t.EntityName))
                {
                    ownerByEntity[t.EntityName] = (registryName, t);
                }
            }
        }

        var edgeMap = new Dictionary<(string From, string To), List<UiDependencyFkDto>>(TupleKeyComparer.Instance);
        var incomingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var outgoingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (registryName, model) in _scannedRegistries)
        {
            foreach (var t in model.Tables)
            {
                if (t.ExcludeFromMigrations)
                {
                    continue;
                }

                foreach (var fk in t.ForeignKeys)
                {
                    var principalQualified = string.IsNullOrWhiteSpace(fk.PrincipalSchema)
                        ? fk.PrincipalTable
                        : $"{fk.PrincipalSchema}.{fk.PrincipalTable}";

                    if (!ownerByTable.TryGetValue(principalQualified, out var target) &&
                        !ownerByEntity.TryGetValue(fk.PrincipalEntityName, out target))
                    {
                        continue;
                    }

                    if (string.Equals(registryName, target.Registry, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var edgeKey = (registryName, target.Registry);
                    if (!edgeMap.TryGetValue(edgeKey, out var fkList))
                    {
                        fkList = [];
                        edgeMap[edgeKey] = fkList;
                    }

                    fkList.Add(new UiDependencyFkDto(
                        ConstraintName: fk.Name,
                        FromEntity: GetShortName(t.EntityName),
                        FromTable: t.QualifiedName,
                        FromColumns: [.. fk.DependentColumns],
                        ToEntity: GetShortName(target.Table.EntityName),
                        ToTable: target.Table.QualifiedName,
                        ToColumns: [.. fk.PrincipalColumns],
                        OnDelete: fk.OnDelete.ToString(),
                        IsCircular: fk.IsCircular));
                }
            }
        }

        foreach (var ((fromReg, toReg), fks) in edgeMap)
        {
            outgoingCounts[fromReg] = outgoingCounts.GetValueOrDefault(fromReg) + fks.Count;
            incomingCounts[toReg] = incomingCounts.GetValueOrDefault(toReg) + fks.Count;
        }

        var nodes = new List<UiDependencyNodeDto>(_scannedRegistries.Count);
        for (var i = 0; i < _scannedRegistries.Count; i++)
        {
            var (registryName, model) = _scannedRegistries[i];
            var schemas = model.Tables
                .Select(static t => string.IsNullOrWhiteSpace(t.Schema) ? "default" : t.Schema)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            nodes.Add(new UiDependencyNodeDto(
                Order: i + 1,
                RegistryName: registryName,
                EntityCount: model.Tables.Count,
                ExcludedCount: model.Tables.Count(static t => t.ExcludeFromMigrations),
                Schemas: schemas,
                IncomingCount: incomingCounts.GetValueOrDefault(registryName),
                OutgoingCount: outgoingCounts.GetValueOrDefault(registryName)));
        }

        var edges = edgeMap
            .OrderBy(static kv => kv.Key.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static kv => kv.Key.To, StringComparer.OrdinalIgnoreCase)
            .Select(static kv => new UiDependencyEdgeDto(
                Id: $"{kv.Key.From}->{kv.Key.To}",
                FromRegistry: kv.Key.From,
                ToRegistry: kv.Key.To,
                ForeignKeys: kv.Value))
            .ToList();

        var stubs = options.EntityStubs
            .OrderBy(static kv => kv.Key.Name, StringComparer.Ordinal)
            .Select(kv =>
            {
                var targetFull = kv.Value.FullName ?? kv.Value.Name;
                ownerByEntity.TryGetValue(targetFull, out var owner);
                return new UiEntityStubDto(
                    StubEntity: kv.Key.FullName ?? kv.Key.Name,
                    ShortStubEntity: kv.Key.Name,
                    TargetEntity: targetFull,
                    ShortTargetEntity: kv.Value.Name,
                    TargetRegistry: owner.Registry,
                    TargetTable: owner.Table?.QualifiedName);
            })
            .ToList();

        _dependenciesCache = new UiDependenciesDto(nodes, edges, stubs);
        return _dependenciesCache;
    }

    public UiDryRunDto BuildDryRun()
    {
        var options = _loaded.Manager.Options;
        var dialect = MigrationDialectRegistry.Get(options);
        var stateDir = new MigrationScriptStore(options).ResolveSnapshotDirectory();

        var items = new List<UiDryRunRegistryDto>(_scannedRegistries.Count + 1);
        var totalAdds = 0;
        var totalAlters = 0;
        var totalDrops = 0;

        if (dialect.SupportsGlobalExtensionsAndEnums &&
            (options.Extensions.Count > 0 || options.Enums.Count > 0))
        {
            var globalSnapshotPath = Path.Combine(stateDir, "_global.json");
            var prevGlobalJson = File.Exists(globalSnapshotPath) ? File.ReadAllText(globalSnapshotPath) : null;
            var globalModel = new DatabaseModel
            {
                Extensions = options.Extensions.Count > 0 ? [.. options.Extensions] : null,
                Enums = options.Enums.Count > 0 ? [.. options.Enums] : null
            };
            var globalDiff = StandaloneSchemaDiffer.GenerateDiff(
                globalModel,
                prevGlobalJson,
                options.Dialect,
                interactiveRenamePrompt: false,
                options.OracleCompatibility);

            var globalItem = MapDryRunRegistry("_global", globalDiff);
            if (globalItem.HasChanges)
            {
                items.Add(globalItem);
                totalAdds += globalItem.AddCount;
                totalAlters += globalItem.AlterCount;
                totalDrops += globalItem.DropCount;
            }
        }

        foreach (var (registryName, model) in _scannedRegistries)
        {
            var snapshotPath = Path.Combine(stateDir, $"{registryName}.json");
            var prevJson = File.Exists(snapshotPath) ? File.ReadAllText(snapshotPath) : null;

            var diff = StandaloneSchemaDiffer.GenerateDiff(
                model,
                prevJson,
                options.Dialect,
                interactiveRenamePrompt: false,
                options.OracleCompatibility);

            var regItem = MapDryRunRegistry(registryName, diff);
            items.Add(regItem);
            totalAdds += regItem.AddCount;
            totalAlters += regItem.AlterCount;
            totalDrops += regItem.DropCount;
        }

        return new UiDryRunDto(
            ChangedRegistryCount: items.Count(static i => i.HasChanges),
            AddCount: totalAdds,
            AlterCount: totalAlters,
            DropCount: totalDrops,
            Registries: items);
    }

    public UiSearchResponseDto SearchSchema(string? query, int limit = 40)
    {
        var q = (query ?? string.Empty).Trim();
        var clampedLimit = Math.Clamp(limit, 1, 100);
        if (q.Length < 2)
        {
            return new UiSearchResponseDto(q, []);
        }

        var hits = new List<UiSearchHitDto>(Math.Min(clampedLimit, 40));

        foreach (var (registryName, model) in _scannedRegistries)
        {
            foreach (var t in model.Tables)
            {
                var shortEntity = GetShortName(t.EntityName);

                foreach (var c in t.Columns)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (c.ColumnName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.PropertyName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        c.SqlType.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "Column",
                            Name: c.ColumnName,
                            Detail: $"{c.PropertyName} ({c.SqlType})",
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                foreach (var ix in t.Indexes)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (ix.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        ix.ColumnNames.Any(col => col.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "Index",
                            Name: ix.Name,
                            Detail: $"{(ix.IsUnique ? "UNIQUE " : string.Empty)}({string.Join(", ", ix.ColumnNames)})",
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                foreach (var fk in t.ForeignKeys)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (fk.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        fk.PrincipalTable.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "ForeignKey",
                            Name: fk.Name,
                            Detail: $"({string.Join(", ", fk.DependentColumns)}) -> {fk.PrincipalTable}({string.Join(", ", fk.PrincipalColumns)})",
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                foreach (var ck in t.CheckConstraints)
                {
                    if (hits.Count >= clampedLimit)
                    {
                        return new UiSearchResponseDto(q, hits);
                    }

                    if (ck.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        ck.Sql.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new UiSearchHitDto(
                            Kind: "CheckConstraint",
                            Name: ck.Name,
                            Detail: ck.Sql,
                            RegistryName: registryName,
                            EntityName: t.EntityName,
                            ShortEntityName: shortEntity,
                            QualifiedName: t.QualifiedName));
                    }
                }

                if (t.ExclusionConstraints is { Count: > 0 })
                {
                    foreach (var ex in t.ExclusionConstraints)
                    {
                        if (hits.Count >= clampedLimit)
                        {
                            return new UiSearchResponseDto(q, hits);
                        }

                        if (ex.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            ex.Method.Contains(q, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add(new UiSearchHitDto(
                                Kind: "ExclusionConstraint",
                                Name: ex.Name,
                                Detail: $"EXCLUDE USING {ex.Method}",
                                RegistryName: registryName,
                                EntityName: t.EntityName,
                                ShortEntityName: shortEntity,
                                QualifiedName: t.QualifiedName));
                        }
                    }
                }
            }
        }

        return new UiSearchResponseDto(q, hits);
    }

    private static UiDryRunRegistryDto MapDryRunRegistry(string registryName, SchemaDiffResult diff)
    {
        var ops = (diff.Operations ?? [])
            .Select(MapDryRunOperation)
            .ToList();

        var addCount = ops.Count(static o => o.Kind == "Add");
        var alterCount = ops.Count(static o => o.Kind == "Alter");
        var dropCount = ops.Count(static o => o.Kind == "Drop");

        return new UiDryRunRegistryDto(
            RegistryName: registryName,
            HasChanges: diff.HasChanges,
            HasDestructiveChanges: dropCount > 0,
            AddCount: addCount,
            AlterCount: alterCount,
            DropCount: dropCount,
            Operations: ops,
            SqlScript: diff.SqlScript,
            DeferredSqlScript: diff.DeferredSqlScript);
    }

    private static UiDryRunOperationDto MapDryRunOperation(SchemaOperation op) => op switch
    {
        EnsureExtensionOp e => new("Add", "Extension", e.Name, $"Ensure extension {e.Name}"),
        DropExtensionOp e => new("Drop", "Extension", e.Name, $"Drop extension {e.Name}"),
        CreateEnumOp e => new("Add", "Enum", Qualify(e.Enum.Schema, e.Enum.Name), $"Create enum ({string.Join(", ", e.Enum.Labels)})"),
        AddEnumValueOp e => new("Alter", "Enum", Qualify(e.Schema, e.EnumName), $"Add enum value '{e.Value}'"),
        DropEnumOp e => new("Drop", "Enum", Qualify(e.Schema, e.Name), $"Drop enum {e.Name}"),
        EnsureSchemaOp s => new("Add", "Schema", s.SchemaName, $"Ensure schema {s.SchemaName}"),
        CreateSequenceOp s => new("Add", "Sequence", Qualify(s.Sequence.Schema, s.Sequence.Name), $"Create sequence (start {s.Sequence.StartValue}, inc {s.Sequence.IncrementBy})"),
        AlterSequenceOp s => new("Alter", "Sequence", Qualify(s.NewSeq.Schema, s.NewSeq.Name), $"Alter sequence {s.NewSeq.Name}"),
        DropSequenceOp s => new("Drop", "Sequence", Qualify(s.Schema, s.Name), $"Drop sequence {s.Name}"),
        CreateTableOp t => new("Add", "Table", t.Table.QualifiedName, $"Create table ({t.Table.Columns.Count} columns)"),
        DropTableOp t => new("Drop", "Table", Qualify(t.Schema, t.TableName), "Drop table"),
        RenameTableOp t => new("Alter", "Table", Qualify(t.NewSchema, t.NewTableName), $"Rename {Qualify(t.OldSchema, t.OldTableName)} -> {Qualify(t.NewSchema, t.NewTableName)}"),
        AddColumnOp c => new("Add", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.Column.ColumnName}", $"{c.Column.SqlType} {(c.Column.IsNullable ? "NULL" : "NOT NULL")}"),
        DropColumnOp c => new("Drop", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.ColumnName}", "Drop column"),
        RenameColumnOp c => new("Alter", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.NewName}", $"Rename {c.OldName} -> {c.NewName}"),
        AlterColumnOp c => new("Alter", "Column", $"{Qualify(c.Schema, c.TableName)}.{c.NewCol.ColumnName}", $"{c.OldCol.SqlType} {(c.OldCol.IsNullable ? "NULL" : "NOT NULL")} -> {c.NewCol.SqlType} {(c.NewCol.IsNullable ? "NULL" : "NOT NULL")}"),
        AddPrimaryKeyOp pk => new("Add", "PrimaryKey", Qualify(pk.Schema, pk.TableName), $"{pk.PrimaryKey.Name} ({string.Join(", ", pk.PrimaryKey.ColumnNames)})"),
        DropPrimaryKeyOp pk => new("Drop", "PrimaryKey", Qualify(pk.Schema, pk.TableName), $"Drop PK {pk.Name}"),
        AddForeignKeyOp fk => new("Add", "ForeignKey", Qualify(fk.Schema, fk.TableName), $"{fk.ForeignKey.Name} -> {Qualify(fk.ForeignKey.PrincipalSchema, fk.ForeignKey.PrincipalTable)}{(fk.ForeignKey.IsCircular ? " (deferred)" : string.Empty)}"),
        DropForeignKeyOp fk => new("Drop", "ForeignKey", Qualify(fk.Schema, fk.TableName), $"Drop FK {fk.Name}"),
        CreateIndexOp ix => new("Add", "Index", Qualify(ix.Schema, ix.TableName), $"{ix.Index.Name} ({string.Join(", ", ix.Index.ColumnNames)})"),
        DropIndexOp ix => new("Drop", "Index", Qualify(ix.Schema, ix.TableName), $"Drop index {ix.Name}"),
        RenameIndexOp ix => new("Alter", "Index", Qualify(ix.Schema, ix.TableName), $"Rename index {ix.OldName} -> {ix.NewName}"),
        AddCheckConstraintOp ck => new("Add", "Check", Qualify(ck.Schema, ck.TableName), $"{ck.Constraint.Name}: {ck.Constraint.Sql}"),
        DropCheckConstraintOp ck => new("Drop", "Check", Qualify(ck.Schema, ck.TableName), $"Drop check {ck.Name}"),
        AddExclusionConstraintOp ex => new("Add", "Exclusion", Qualify(ex.Schema, ex.TableName), $"{ex.Constraint.Name} (USING {ex.Constraint.Method})"),
        DropExclusionConstraintOp ex => new("Drop", "Exclusion", Qualify(ex.Schema, ex.TableName), $"Drop exclusion {ex.Name}"),
        RenameConstraintOp rc => new("Alter", "Constraint", Qualify(rc.Schema, rc.TableName), $"Rename constraint {rc.OldName} -> {rc.NewName}"),
        SetTableCommentOp tc => new("Alter", "Comment", Qualify(tc.Schema, tc.TableName), string.IsNullOrEmpty(tc.Comment) ? "Clear table comment" : "Update table comment"),
        SetColumnCommentOp cc => new("Alter", "Comment", $"{Qualify(cc.Schema, cc.TableName)}.{cc.ColumnName}", string.IsNullOrEmpty(cc.Comment) ? "Clear column comment" : "Update column comment"),
        SetTableLoggedOp tl => new("Alter", "Storage", Qualify(tl.Schema, tl.TableName), tl.IsUnlogged ? "Set UNLOGGED" : "Set LOGGED"),
        SetTableTablespaceOp tt => new("Alter", "Storage", Qualify(tt.Schema, tt.TableName), $"Set tablespace {tt.Tablespace}"),
        AlterTableStorageParametersOp sp => new("Alter", "Storage", Qualify(sp.Schema, sp.TableName), "Alter table storage parameters"),
        SetIndexTablespaceOp it => new("Alter", "Storage", Qualify(it.Schema, it.IndexName), $"Set index tablespace {it.Tablespace}"),
        AlterIndexStorageParametersOp isp => new("Alter", "Storage", Qualify(isp.Schema, isp.IndexName), "Alter index storage parameters"),
        _ => new("Alter", "Schema", op.GetType().Name, op.GetType().Name)
    };

    private static string Qualify(string? schema, string name) =>
        string.IsNullOrWhiteSpace(schema) ? name : $"{schema}.{name}";

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
                DependentProperties: [.. fk.DependentProperties],
                PrincipalEntityName: fk.PrincipalEntityName,
                PrincipalSchema: fk.PrincipalSchema,
                PrincipalTable: fk.PrincipalTable,
                PrincipalQualifiedName: string.IsNullOrWhiteSpace(fk.PrincipalSchema)
                    ? fk.PrincipalTable
                    : $"{fk.PrincipalSchema}.{fk.PrincipalTable}",
                PrincipalColumns: [.. fk.PrincipalColumns],
                PrincipalProperties: [.. fk.PrincipalProperties],
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
                IncludedProperties: ix.IncludedPropertyNames is null ? null : [.. ix.IncludedPropertyNames],
                OperatorsByColumn: ix.OperatorsByColumn,
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
