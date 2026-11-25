using System.Data.Common;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class SchemaDriftVerifier
{
    private readonly DbMigratorOptions _options;
    private readonly IMigrationDialect _dialect;

    public SchemaDriftVerifier(DbMigratorOptions options)
    {
        _options = options;
        _dialect = MigrationDialectRegistry.Get(options);
    }

    public async Task<IReadOnlyList<SchemaDriftItem>> VerifyAsync(DbConnection conn, CancellationToken ct)
    {
        var sortedRegistries = RegistryDependencyResolver.SortTopologically(_options);

        var expectedTables = new List<(string RegistryName, TableSchema Table)>();
        foreach (var registry in sortedRegistries)
        {
            var schema = SchemaScanner.ScanRegistry(registry, _options, sortedRegistries);

            var registryName = registry.GetType().Name;
            foreach (var table in schema.Tables)
            {
                if (!table.ExcludeFromMigrations)
                {
                    expectedTables.Add((registryName, table));
                }
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
                if (!liveCols.TryGetValue(col.ColumnName, out var liveCol))
                {
                    drift.Add(new SchemaDriftItem(
                        registryName,
                        table.QualifiedName,
                        col.ColumnName,
                        "MissingColumn",
                        $"Column '{col.ColumnName}' ({col.SqlType}) is missing in database."));
                    continue;
                }

                if (liveCol.IsNullable != col.IsNullable)
                {
                    var expectedNullText = col.IsNullable ? "NULL" : "NOT NULL";
                    var liveNullText = liveCol.IsNullable ? "NULL" : "NOT NULL";
                    drift.Add(new SchemaDriftItem(
                        registryName,
                        table.QualifiedName,
                        col.ColumnName,
                        "NullabilityMismatch",
                        $"Expected {expectedNullText}, found {liveNullText} in database."));
                }

                if (!_dialect.AreSqlTypesCompatible(col.SqlType, liveCol.SqlType))
                {
                    drift.Add(new SchemaDriftItem(
                        registryName,
                        table.QualifiedName,
                        col.ColumnName,
                        "TypeMismatch",
                        $"Expected SQL type '{col.SqlType}', found '{liveCol.SqlType}' in database."));
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
