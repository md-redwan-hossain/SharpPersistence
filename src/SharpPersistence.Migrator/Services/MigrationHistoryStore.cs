using System.Data.Common;
using System.Globalization;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationHistoryStore
{
    private readonly MigrationManagerOptions _options;
    private readonly IMigrationDialect _dialect;

    public MigrationHistoryStore(MigrationManagerOptions options)
    {
        _options = options;
        _dialect = MigrationDialectRegistry.Get(options);
    }

    public Task EnsureHistoryTableAsync(DbConnection conn, CancellationToken ct) =>
        _dialect.EnsureHistoryTableAsync(conn, _options, ct);

    public Task<bool> HistoryTableExistsAsync(DbConnection conn, CancellationToken ct) =>
        _dialect.HistoryTableExistsAsync(conn, _options, ct);

    public async Task<IReadOnlyList<AppliedMigrationRecord>> ReadAppliedAsync(DbConnection conn, CancellationToken ct)
    {
        var records = new List<AppliedMigrationRecord>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT \"registry_name\", \"script_name\", \"checksum_sha256\", \"applied_at\" FROM {Qualify(_options.HistorySchema, _options.HistoryTableName)} ORDER BY \"applied_at\", \"registry_name\", \"script_name\"";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var reg = reader.GetString(0);
            var script = reader.GetString(1);
            var checksum = reader.GetString(2);
            var rawAppliedAt = reader.GetValue(3);
            var appliedAt = rawAppliedAt switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                _ => DateTimeOffset.Parse(rawAppliedAt.ToString()!, CultureInfo.InvariantCulture)
            };
            records.Add(new AppliedMigrationRecord(reg, script, checksum, appliedAt));
        }

        return records;
    }

    public Task RecordAppliedAsync(
        DbConnection conn,
        string registryName,
        string scriptName,
        string checksum,
        CancellationToken ct,
        DbTransaction? transaction = null) =>
        _dialect.RecordAppliedAsync(conn, _options, registryName, scriptName, checksum, ct, transaction);

    public Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct) =>
        _dialect.ReadEfCoreMigrationIdsAsync(conn, ct);
}
