using System.Data.Common;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Dialects;

internal readonly record struct LiveColumnInfo(bool IsNullable, string SqlType);

internal interface ISqlDialectMetadata
{
    SqlDialect Dialect { get; }
    bool SupportsGlobalExtensionsAndEnums { get; }
    bool SupportsComputedStorageMode { get; }
    string ProviderPackageHint { get; }

    string NormalizeSchemaName(string? schema);
    bool AreSqlTypesCompatible(string depSqlType, string prinSqlType);
    string ResolveSequenceSqlType(Type clrType);
}

internal interface IMigrationDialect : ISqlDialectMetadata
{
    string PingSql { get; }
    bool SupportsTransactionalDdl { get; }

    ISqlDialectGenerator CreateDdlGenerator();
    DbProviderFactory? ResolveDefaultProviderFactory();
    IEnumerable<string> SplitScriptStatements(string sql);
    string FormatEmptyMigration(string migrationName);
    bool TryStripOuterTransaction(string sql, out string innerSql);

    Task<IAsyncDisposable?> AcquireLockAsync(DbConnection conn, DbMigratorOptions options, CancellationToken ct);
    Task EnsureHistoryTableAsync(DbConnection conn, DbMigratorOptions options, CancellationToken ct);
    Task<bool> HistoryTableExistsAsync(DbConnection conn, DbMigratorOptions options, CancellationToken ct);
    Task RecordAppliedAsync(
        DbConnection conn,
        DbMigratorOptions options,
        string registryName,
        string scriptName,
        string checksum,
        CancellationToken ct,
        DbTransaction? transaction = null);
    Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct);
    Task<Dictionary<(string Schema, string Table), Dictionary<string, LiveColumnInfo>>> ReadLiveColumnsAsync(
        DbConnection conn,
        IReadOnlyList<(string RegistryName, TableSchema Table)> expectedTables,
        CancellationToken ct);
}

internal static class MigrationDialectRegistry
{
    private static readonly PostgresMigrationDialect PostgresInstance = new();

    public static IMigrationDialect Get(DbMigratorOptions options) =>
        Get(options.Dialect, options.OracleCompatibility);

    public static IMigrationDialect Get(
        SqlDialect dialect,
        OracleDbCompatibility oracleCompatibility = OracleDbCompatibility.Oracle19) =>
        dialect switch
        {
            SqlDialect.PostgreSql => PostgresInstance,
            SqlDialect.Oracle => new OracleMigrationDialect(oracleCompatibility),
            _ => throw new NotSupportedException($"SQL dialect '{dialect}' is not supported yet.")
        };

    internal static DbProviderFactory? ResolveProviderFactory(string typeName, string assemblyName)
    {
        var type = Type.GetType($"{typeName}, {assemblyName}");
        if (type is null)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase));
            type = asm?.GetType(typeName);
        }

        if (type is null)
        {
            return null;
        }

        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;

        if (type.GetField("Instance", flags)?.GetValue(null) is DbProviderFactory fieldFactory)
        {
            return fieldFactory;
        }

        return type.GetProperty("Instance", flags)?.GetValue(null) as DbProviderFactory;
    }

    internal static void AddLiveColumn(
        Dictionary<(string Schema, string Table), Dictionary<string, LiveColumnInfo>> result,
        string schema,
        string table,
        string column,
        bool isNullable,
        string sqlType)
    {
        var key = (schema, table);
        if (!result.TryGetValue(key, out var cols))
        {
            cols = new Dictionary<string, LiveColumnInfo>(StringComparer.Ordinal);
            result[key] = cols;
        }

        cols[column] = new LiveColumnInfo(isNullable, sqlType);
    }

    internal sealed class AsyncDisposableLock(Func<Task> release) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await release();
    }
}
