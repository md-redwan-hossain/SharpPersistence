using System.Data.Common;
using System.Globalization;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationHistoryStore
{
    private readonly DbMigratorOptions _options;
    private readonly IMigrationDialect _dialect;

    public MigrationHistoryStore(DbMigratorOptions options)
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
            $"SELECT \"script_id\", \"script_name\", \"checksum_sha256\", \"applied_at\" FROM {Qualify(_options.HistorySchema, _options.HistoryTableName)} ORDER BY \"applied_at\", \"script_id\"";

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var rawId = reader.GetValue(0);
            var scriptId = rawId switch
            {
                Guid g => g,
                _ => Guid.Parse(rawId.ToString()!)
            };
            var script = reader.GetString(1);
            var checksum = reader.GetString(2);
            var rawAppliedAt = reader.GetValue(3);
            var appliedAt = rawAppliedAt switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                _ => DateTimeOffset.Parse(rawAppliedAt.ToString()!, CultureInfo.InvariantCulture)
            };
            records.Add(new AppliedMigrationRecord(scriptId, script, checksum, appliedAt));
        }

        return records;
    }

    public Task RecordAppliedAsync(
        DbConnection conn,
        Guid scriptId,
        string scriptName,
        string checksum,
        CancellationToken ct,
        DbTransaction? transaction = null) =>
        _dialect.RecordAppliedAsync(conn, _options, scriptId, scriptName, checksum, ct, transaction);

    public Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct) =>
        _dialect.ReadEfCoreMigrationIdsAsync(conn, ct);
}
