using System.Data.Common;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class SchemaDriftVerifier
{
    private readonly MigrationManagerOptions _options;

    public SchemaDriftVerifier(MigrationManagerOptions options)
    {
        _options = options;
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

        var liveTables = await ReadLiveColumnsAsync(conn, expectedTables, ct);
        var drift = new List<SchemaDriftItem>();

        foreach (var (registryName, table) in expectedTables)
        {
            var lookupSchema = _options.Dialect == SqlDialect.PostgreSql
                ? (string.IsNullOrWhiteSpace(table.Schema) ? "public" : table.Schema)
                : (string.IsNullOrWhiteSpace(table.Schema) ? string.Empty : table.Schema);

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

    private async Task<Dictionary<(string Schema, string Table), Dictionary<string, bool>>> ReadLiveColumnsAsync(
        DbConnection conn,
        IReadOnlyList<(string RegistryName, TableSchema Table)> expectedTables,
        CancellationToken ct)
    {
        var result = new Dictionary<(string Schema, string Table), Dictionary<string, bool>>();

        if (_options.Dialect == SqlDialect.PostgreSql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT table_schema, table_name, column_name, is_nullable
                FROM information_schema.columns
                WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
                """;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                AddColumn(result, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    string.Equals(reader.GetString(3), "YES", StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (_options.Dialect == SqlDialect.Oracle)
        {
            var hasUnqualified = expectedTables.Any(t => string.IsNullOrWhiteSpace(t.Table.Schema));
            var explicitSchemas = expectedTables
                .Select(t => t.Table.Schema)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (hasUnqualified)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT TABLE_NAME, COLUMN_NAME, NULLABLE FROM USER_TAB_COLUMNS";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    AddColumn(result, string.Empty, reader.GetString(0), reader.GetString(1),
                        string.Equals(reader.GetString(2), "Y", StringComparison.OrdinalIgnoreCase));
                }
            }

            foreach (var owner in explicitSchemas)
            {
                await using var cmd = conn.CreateCommand();
                OracleCommandHelper.EnableBindByName(cmd);
                cmd.CommandText = "SELECT OWNER, TABLE_NAME, COLUMN_NAME, NULLABLE FROM ALL_TAB_COLUMNS WHERE OWNER = :owner";
                var p = cmd.CreateParameter();
                p.ParameterName = "owner";
                p.Value = owner;
                cmd.Parameters.Add(p);

                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    AddColumn(result, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        string.Equals(reader.GetString(3), "Y", StringComparison.OrdinalIgnoreCase));
                }
            }
        }

        return result;
    }

    private static void AddColumn(
        Dictionary<(string Schema, string Table), Dictionary<string, bool>> result,
        string schema,
        string table,
        string column,
        bool isNullable)
    {
        var key = (schema, table);
        if (!result.TryGetValue(key, out var cols))
        {
            cols = new Dictionary<string, bool>(StringComparer.Ordinal);
            result[key] = cols;
        }

        cols[column] = isNullable;
    }
}
