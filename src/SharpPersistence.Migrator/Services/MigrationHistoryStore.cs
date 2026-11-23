using System.Data.Common;
using System.Globalization;
using SharpPersistence.Enums;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Models;
using static SharpPersistence.Migrator.Dialects.SqlFormatHelper;

namespace SharpPersistence.Migrator.Services;

internal sealed class MigrationHistoryStore
{
    private readonly MigrationManagerOptions _options;

    public MigrationHistoryStore(MigrationManagerOptions options)
    {
        _options = options;
    }

    public async Task EnsureHistoryTableAsync(DbConnection conn, CancellationToken ct)
    {
        var table = QualifyHistoryTable();
        if (_options.Dialect == SqlDialect.Oracle)
        {
            var createSql =
                $"CREATE TABLE {table} (" +
                "\"registry_name\" NVARCHAR2(256) NOT NULL, " +
                "\"script_name\" NVARCHAR2(256) NOT NULL, " +
                "\"checksum_sha256\" VARCHAR2(64) NOT NULL, " +
                "\"applied_at\" TIMESTAMP(7) WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL, " +
                $"CONSTRAINT \"pk_{_options.HistoryTableName}\" PRIMARY KEY (\"registry_name\", \"script_name\"))";

            await using var oracleCmd = conn.CreateCommand();
            oracleCmd.CommandText =
                $"BEGIN EXECUTE IMMEDIATE '{EscapeLiteral(createSql)}'; EXCEPTION WHEN OTHERS THEN IF SQLCODE != -955 THEN RAISE; END IF; END;";
            await oracleCmd.ExecuteNonQueryAsync(ct);
            return;
        }

        var schemaCreate = !string.IsNullOrWhiteSpace(_options.HistorySchema)
            ? $"CREATE SCHEMA IF NOT EXISTS {Quote(_options.HistorySchema)};{Environment.NewLine}"
            : string.Empty;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"""
            {schemaCreate}CREATE TABLE IF NOT EXISTS {table} (
                "registry_name" text NOT NULL,
                "script_name" text NOT NULL,
                "checksum_sha256" text NOT NULL,
                "applied_at" timestamp with time zone NOT NULL DEFAULT now(),
                CONSTRAINT "pk_{_options.HistoryTableName}" PRIMARY KEY ("registry_name", "script_name")
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AppliedMigrationRecord>> ReadAppliedAsync(DbConnection conn, CancellationToken ct)
    {
        var records = new List<AppliedMigrationRecord>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT \"registry_name\", \"script_name\", \"checksum_sha256\", \"applied_at\" FROM {QualifyHistoryTable()} ORDER BY \"applied_at\", \"registry_name\", \"script_name\"";

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

    public async Task RecordAppliedAsync(
        DbConnection conn,
        string registryName,
        string scriptName,
        string checksum,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        var isOracle = _options.Dialect == SqlDialect.Oracle;
        if (isOracle)
        {
            OracleCommandHelper.EnableBindByName(cmd);
        }

        var paramPrefix = isOracle ? ":" : "@";
        cmd.CommandText = isOracle
            ? $"MERGE INTO {QualifyHistoryTable()} t " +
              $"USING (SELECT {paramPrefix}reg AS \"registry_name\", {paramPrefix}script AS \"script_name\", {paramPrefix}sum AS \"checksum_sha256\" FROM DUAL) s " +
              "ON (t.\"registry_name\" = s.\"registry_name\" AND t.\"script_name\" = s.\"script_name\") " +
              "WHEN NOT MATCHED THEN INSERT (\"registry_name\", \"script_name\", \"checksum_sha256\") " +
              "VALUES (s.\"registry_name\", s.\"script_name\", s.\"checksum_sha256\")"
            : $"INSERT INTO {QualifyHistoryTable()} (\"registry_name\", \"script_name\", \"checksum_sha256\") VALUES (@reg, @script, @sum) ON CONFLICT (\"registry_name\", \"script_name\") DO NOTHING;";

        var p1 = cmd.CreateParameter();
        p1.ParameterName = isOracle ? "reg" : "@reg";
        p1.Value = registryName;
        cmd.Parameters.Add(p1);

        var p2 = cmd.CreateParameter();
        p2.ParameterName = isOracle ? "script" : "@script";
        p2.Value = scriptName;
        cmd.Parameters.Add(p2);

        var p3 = cmd.CreateParameter();
        p3.ParameterName = isOracle ? "sum" : "@sum";
        p3.Value = checksum;
        cmd.Parameters.Add(p3);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ReadEfCoreMigrationIdsAsync(DbConnection conn, CancellationToken ct)
    {
        await using var existsCmd = conn.CreateCommand();
        if (_options.Dialect == SqlDialect.Oracle)
        {
            existsCmd.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = '__EFMigrationsHistory'";
            var count = Convert.ToInt32(await existsCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (count == 0)
            {
                return [];
            }
        }
        else
        {
            existsCmd.CommandText = "SELECT to_regclass('\"__EFMigrationsHistory\"') IS NOT NULL;";
            var existsObj = await existsCmd.ExecuteScalarAsync(ct);
            if (existsObj is not true)
            {
                return [];
            }
        }

        var ids = new List<string>();
        await using var readCmd = conn.CreateCommand();
        readCmd.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"";
        await using var reader = await readCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private string QualifyHistoryTable() =>
        Qualify(_options.HistorySchema, _options.HistoryTableName);
}
