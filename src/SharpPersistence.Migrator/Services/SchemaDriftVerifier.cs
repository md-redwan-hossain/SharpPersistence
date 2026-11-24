using System.Data.Common;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class SchemaDriftVerifier
{
    private readonly MigrationManagerOptions _options;
    private readonly IMigrationDialect _dialect;

    public SchemaDriftVerifier(MigrationManagerOptions options)
    {
        _options = options;
        _dialect = MigrationDialectRegistry.Get(options);
    }

    public async Task<IReadOnlyList<SchemaDriftItem>> VerifyAsync(DbConnection conn, CancellationToken ct)
    {
        var sortedRegistries = RegistryDependencyResolver.SortTopologically(
            _options.Registries,
            _options.Dialect,
            _options.IdentifierCase,
            _options.PropertyConventions);

        var expectedTables = new List<(string RegistryName, TableSchema Table)>();
        foreach (var registry in sortedRegistries)
        {
            var schema = SchemaScanner.ScanRegistry(
                registry,
                _options.Dialect,
                _options.IdentifierCase,
                sortedRegistries,
                _options.PropertyConventions,
                _options.Enums,
                _options.CheckConstraintOptions);

            var registryName = registry.GetType().Name;
            foreach (var table in schema.Tables)
            {
                expectedTables.Add((registryName, table));
            }
        }

        if (expectedTables.Count == 0)
        {
            return [];
        }

        var liveTables = await _dialect.ReadLiveColumnsAsync(conn, expectedTables, ct);
        var drift = new List<SchemaDriftItem>();

        foreach (var (registryName, table) in expectedTables)
        {
            var lookupSchema = _dialect.NormalizeSchemaName(table.Schema);
            var key = (lookupSchema, table.TableName);
            if (!liveTables.TryGetValue(key, out var liveCols))
            {
                drift.Add(new SchemaDriftItem(
                    registryName,
                    table.QualifiedName,
                    null,
                    "MissingTable",
                    $"Table '{table.QualifiedName}' does not exist in the target database."));
                continue;
            }

            var expectedCols = table.Columns
                .Where(c => !c.IsSystemColumn)
                .ToDictionary(c => c.ColumnName, StringComparer.Ordinal);

            foreach (var col in expectedCols.Values)
            {
                if (!liveCols.TryGetValue(col.ColumnName, out var liveNullable))
                {
                    drift.Add(new SchemaDriftItem(
                        registryName,
                        table.QualifiedName,
                        col.ColumnName,
                        "MissingColumn",
                        $"Column '{col.ColumnName}' ({col.SqlType}) is missing in database."));
                    continue;
                }

                if (liveNullable != col.IsNullable)
                {
                    var expectedNullText = col.IsNullable ? "NULL" : "NOT NULL";
                    var liveNullText = liveNullable ? "NULL" : "NOT NULL";
                    drift.Add(new SchemaDriftItem(
                        registryName,
                        table.QualifiedName,
                        col.ColumnName,
                        "NullabilityMismatch",
                        $"Expected {expectedNullText}, found {liveNullText} in database."));
                }
            }

            foreach (var liveColName in liveCols.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                if (!expectedCols.ContainsKey(liveColName))
                {
                    drift.Add(new SchemaDriftItem(
                        registryName,
                        table.QualifiedName,
                        liveColName,
                        "ExtraColumn",
                        $"Column '{liveColName}' exists in database but is not mapped in C# schema."));
                }
            }
        }

        return drift;
    }
}
