using System.Data.Common;
using System.Diagnostics;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Dialects;
using SharpPersistence.Migrator.Models;

namespace SharpPersistence.Migrator.Services;

internal sealed class DoctorDatabaseValidator(
    DbMigratorOptions options,
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
            (conn, ownsConnection) = await connectionOpener.OpenAsync(ct).ConfigureAwait(false);

            await using (var pingCmd = conn.CreateCommand())
            {
                pingCmd.CommandText = dialect.PingSql;
                _ = await pingCmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
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
            var root = MigrationDoctor.UnwrapException(ex);
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Failed to connect to database: {root.Message}",
                Hint: "Verify your connection string, credentials, and network access."));
            if (ownsConnection && conn is not null)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }

            return checks;
        }

        try
        {
            var historyExists = await CheckHistoryTableAsync(conn, registryFilter, checks, ct).ConfigureAwait(false);
            await CheckPermissionsAsync(conn, registryFilter, historyExists, checks, ct).ConfigureAwait(false);

            if (!hasSchemaErrors)
            {
                await CheckLiveSchemaSyncAsync(conn, registryFilter, checks, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var root = MigrationDoctor.UnwrapException(ex);
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Error,
                $"Live database verification failed: {root.Message}"));
        }
        finally
        {
            if (ownsConnection)
            {
                await conn.DisposeAsync().ConfigureAwait(false);
            }
        }

        return checks;
    }

    private async Task<bool> CheckHistoryTableAsync(
        DbConnection conn,
        string? registryFilter,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var historyExists = await historyStore.HistoryTableExistsAsync(conn, ct).ConfigureAwait(false);
        if (!historyExists)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Warning,
                $"Migration history table '{DbMigratorConsts.HistoryTableName}' does not exist yet",
                Hint: "Run `db-migrator migrate` to initialize it and apply pending scripts."));
            return false;
        }

        var applied = await historyStore.ReadAppliedAsync(conn, ct).ConfigureAwait(false);
        try
        {
            var statuses = await scriptStore.BuildStatusItemsAsync(applied, ct).ConfigureAwait(false);
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
                    $"Migration history table '{DbMigratorConsts.HistoryTableName}' found {mismatchCount} modified script(s) after being applied.",
                    Hint: "Run `db-migrator status` to inspect checksum mismatches."));
            }
            else if (pendingCount > 0)
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Warning,
                    $"Migration history table '{DbMigratorConsts.HistoryTableName}' verified ({appliedCount} applied, {pendingCount} pending)",
                    Hint: "Run `db-migrator migrate` to apply pending scripts."));
            }
            else
            {
                checks.Add(new DoctorCheckItem(
                    DoctorSeverity.Pass,
                    $"Migration history table '{DbMigratorConsts.HistoryTableName}' verified ({appliedCount} applied, 0 pending)"));
            }

            var orderWarnings = new MigrationRunlistManager(options, scriptStore)
                .Validate(applied, includeDdlValidation: false)
                .Where(c => c.Severity == DoctorSeverity.Warning);
            checks.AddRange(orderWarnings);
        }
        catch (Exception ex)
        {
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Warning,
                $"Could not verify pending scripts against '{DbMigratorConsts.HistoryTableName}': {ex.Message}",
                Hint: "Fix the runlist errors above or run `db-migrator runlist sync`."));
        }

        return true;
    }

    private async Task CheckPermissionsAsync(
        DbConnection conn,
        string? registryFilter,
        bool historyExists,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        try
        {
            var scripts = scriptStore.DiscoverScripts();
            if (!string.IsNullOrWhiteSpace(registryFilter))
            {
                scripts = scripts
                    .Where(s => string.Equals(s.RegistryName, registryFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var requirements = SqlPermissionRequirementExtractor.Create(options, dialect, historyExists);
            foreach (var (_, _, _, fullPath) in scripts)
            {
                var body = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
                SqlPermissionRequirementExtractor.AddScript(requirements, body, dialect);
            }

            var permissionChecks = await dialect.CheckPermissionsAsync(conn, requirements, ct).ConfigureAwait(false);
            checks.AddRange(permissionChecks);
        }
        catch (Exception ex)
        {
            var root = MigrationDoctor.UnwrapException(ex);
            checks.Add(new DoctorCheckItem(
                DoctorSeverity.Warning,
                $"Could not verify database permissions: {root.Message}"));
        }
    }

    private async Task CheckLiveSchemaSyncAsync(
        DbConnection conn,
        string? registryFilter,
        List<DoctorCheckItem> checks,
        CancellationToken ct)
    {
        var diffs = await driftVerifier.VerifyAsync(conn, ct).ConfigureAwait(false);
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
}
