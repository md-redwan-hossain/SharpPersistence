using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class DoctorDatabaseValidator(
    MigrationManagerOptions options,
    IMigrationDialect dialect,
    MigrationConnectionOpener connectionOpener,
    MigrationScriptStore scriptStore,
    MigrationHistoryStore historyStore,
    SchemaDriftVerifier driftVerifier)
{
    public async Task<List<DoctorCheckItem>> ValidateAsync(
        string? registryFilter,
        bool hasSchemaErrors,
        CancellationToken ct)
    {
        var checks = new List<DoctorCheckItem>();
        DbConnection? conn = null;
        var ownsConnection = false;

        try
        {
            var sw = Stopwatch.StartNew();
            (conn, ownsConnection) = await connectionOpener.OpenAsync(ct);

            await using (var pingCmd = conn.CreateCommand())
            {
                pingCmd.CommandText = dialect.PingSql;
                _ = await pingCmd.ExecuteScalarAsync(ct);
            }

            sw.Stop();
            var version = string.IsNullOrWhiteSpace(conn.ServerVersion) ? string.Empty : $" {conn.ServerVersion}";
            var dbInfo = string.IsNullOrWhiteSpace(conn.Database) ? string.Empty : $"db: {conn.Database}, ";
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                $"Connected to {options.Dialect}{version} ({dbInfo}{sw.ElapsedMilliseconds} ms)"));
        }
        catch (Exception ex)
        {
            var root = UnwrapException(ex);
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Failed to connect to database: {root.Message}",
                Hint: "Verify your connection string, credentials, and network access."));
            if (ownsConnection && conn is not null)
            {
                await conn.DisposeAsync();
            }

            return checks;
        }

        try
        {
            await CheckHistoryTableAsync(conn, registryFilter, checks, ct);

            if (!hasSchemaErrors)
            {
                await CheckLiveSchemaSyncAsync(conn, registryFilter, checks, ct);
            }
        }
        catch (Exception ex)
        {
            var root = UnwrapException(ex);
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Live database verification failed: {root.Message}"));
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync();
            }
        }

        return checks;
    }

    private async Task CheckHistoryTableAsync(
        DbConnection conn,
        string? registryFilter,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var historyExists = await historyStore.HistoryTableExistsAsync(conn, ct);
        if (!historyExists)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Warning,
                $"Migration history table '{options.HistoryTableName}' does not exist yet",
                Hint: "Run `db-migrator migrate` to initialize it and apply pending scripts."));
            return;
        }

        var applied = await historyStore.ReadAppliedAsync(conn, ct);
        var statuses = await scriptStore.BuildStatusItemsAsync(applied, ct);
        if (!string.IsNullOrWhiteSpace(registryFilter))
        {
            statuses = statuses
                .Where(s => string.Equals(s.RegistryName, registryFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var appliedCount = statuses.Count(s => s.IsApplied);
        var pendingCount = statuses.Count(s => !s.IsApplied);
        var mismatchCount = statuses.Count(s => s.HasChecksumMismatch);

        if (mismatchCount > 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Migration history table '{options.HistoryTableName}' found {mismatchCount} modified script(s) after being applied.",
                Hint: "Run `db-migrator status` to inspect checksum mismatches."));
        }
        else if (pendingCount > 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Warning,
                $"Migration history table '{options.HistoryTableName}' verified ({appliedCount} applied, {pendingCount} pending)",
                Hint: "Run `db-migrator migrate` to apply pending scripts."));
        }
        else
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                $"Migration history table '{options.HistoryTableName}' verified ({appliedCount} applied, 0 pending)"));
        }
    }

    private async Task CheckLiveSchemaSyncAsync(
        DbConnection conn,
        string? registryFilter,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var diffs = await driftVerifier.VerifyAsync(conn, ct);
        if (!string.IsNullOrWhiteSpace(registryFilter))
        {
            diffs = diffs
                .Where(d => string.Equals(d.RegistryName, registryFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (diffs.Count == 0)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Pass,
                "Live database matches your migration scripts"));
            return;
        }

        var diffWord = diffs.Count == 1 ? "difference" : "differences";
        checks.Add(new DoctorCheckItem(
            DoctorSeverity.Error,
            $"Live database does not match your migration scripts ({diffs.Count} unexpected {diffWord})"));

        foreach (var diff in diffs)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"[{diff.RegistryName}] {diff.TableName}: {diff.Details}"));
        }
    }

    private static Exception UnwrapException(Exception ex) =>
        ex is TargetInvocationException tie && tie.InnerException is not null
            ? tie.InnerException
            : ex;
}
