using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Cli.Ui;

internal static class UiDependencyGraphBuilder
{
    public static UiDependenciesDto Build(
        IReadOnlyList<(string RegistryName, DatabaseModel Model)> scannedRegistries,
        DbMigratorOptions options)
    {
        var ownerByTable = new Dictionary<string, (string Registry, TableSchema Table)>(StringComparer.OrdinalIgnoreCase);
        var ownerByEntity = new Dictionary<string, (string Registry, TableSchema Table)>(StringComparer.OrdinalIgnoreCase);

        foreach (var (registryName, model) in scannedRegistries)
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

        var edgeMap = new Dictionary<(string From, string To), List<UiDependencyFkDto>>(UiDataService.TupleKeyComparer.Instance);
        var incomingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var outgoingCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (registryName, model) in scannedRegistries)
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
                        FromEntity: UiDataService.GetShortName(t.EntityName),
                        FromTable: t.QualifiedName,
                        FromColumns: [.. fk.DependentColumns],
                        ToEntity: UiDataService.GetShortName(target.Table.EntityName),
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

        var nodes = new List<UiDependencyNodeDto>(scannedRegistries.Count);
        for (var i = 0; i < scannedRegistries.Count; i++)
        {
            var (registryName, model) = scannedRegistries[i];
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

        var replacements = options.EntityReplacements
            .OrderBy(static kv => kv.Key.Name, StringComparer.Ordinal)
            .Select(kv =>
            {
                var targetFull = kv.Value.FullName ?? kv.Value.Name;
                ownerByEntity.TryGetValue(targetFull, out var owner);
                return new UiEntityTypeReplacementDto(
                    SourceEntity: kv.Key.FullName ?? kv.Key.Name,
                    ShortSourceEntity: kv.Key.Name,
                    TargetEntity: targetFull,
                    ShortTargetEntity: kv.Value.Name,
                    TargetRegistry: owner.Registry,
                    TargetTable: owner.Table?.QualifiedName);
            })
            .ToList();

        return new UiDependenciesDto(nodes, edges, replacements);
    }
}
