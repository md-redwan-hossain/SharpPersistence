namespace SharpPersistence.Migrator.PostgreSql;

public enum PostgresStorageParameter : byte
{
    Fillfactor,
    ToastTupleTarget,
    ParallelWorkers,
    AutovacuumEnabled,
    VacuumIndexCleanup,
    VacuumTruncate,
    AutovacuumVacuumThreshold,
    AutovacuumVacuumInsertThreshold,
    AutovacuumVacuumScaleFactor,
    AutovacuumVacuumInsertScaleFactor,
    AutovacuumAnalyzeThreshold,
    AutovacuumAnalyzeScaleFactor,
    AutovacuumVacuumCostDelay,
    AutovacuumVacuumCostLimit,
    AutovacuumFreezeMinAge,
    AutovacuumFreezeMaxAge,
    AutovacuumFreezeTableAge,
    AutovacuumMultixactFreezeMinAge,
    AutovacuumMultixactFreezeMaxAge,
    AutovacuumMultixactFreezeTableAge,
    LogAutovacuumMinDuration,
    UserCatalogTable
}

internal static class PostgresStorageParameterExtensions
{
    public static string ToSqlName(this PostgresStorageParameter parameter) => parameter switch
    {
        PostgresStorageParameter.Fillfactor => "fillfactor",
        PostgresStorageParameter.ToastTupleTarget => "toast_tuple_target",
        PostgresStorageParameter.ParallelWorkers => "parallel_workers",
        PostgresStorageParameter.AutovacuumEnabled => "autovacuum_enabled",
        PostgresStorageParameter.VacuumIndexCleanup => "vacuum_index_cleanup",
        PostgresStorageParameter.VacuumTruncate => "vacuum_truncate",
        PostgresStorageParameter.AutovacuumVacuumThreshold => "autovacuum_vacuum_threshold",
        PostgresStorageParameter.AutovacuumVacuumInsertThreshold => "autovacuum_vacuum_insert_threshold",
        PostgresStorageParameter.AutovacuumVacuumScaleFactor => "autovacuum_vacuum_scale_factor",
        PostgresStorageParameter.AutovacuumVacuumInsertScaleFactor => "autovacuum_vacuum_insert_scale_factor",
        PostgresStorageParameter.AutovacuumAnalyzeThreshold => "autovacuum_analyze_threshold",
        PostgresStorageParameter.AutovacuumAnalyzeScaleFactor => "autovacuum_analyze_scale_factor",
        PostgresStorageParameter.AutovacuumVacuumCostDelay => "autovacuum_vacuum_cost_delay",
        PostgresStorageParameter.AutovacuumVacuumCostLimit => "autovacuum_vacuum_cost_limit",
        PostgresStorageParameter.AutovacuumFreezeMinAge => "autovacuum_freeze_min_age",
        PostgresStorageParameter.AutovacuumFreezeMaxAge => "autovacuum_freeze_max_age",
        PostgresStorageParameter.AutovacuumFreezeTableAge => "autovacuum_freeze_table_age",
        PostgresStorageParameter.AutovacuumMultixactFreezeMinAge => "autovacuum_multixact_freeze_min_age",
        PostgresStorageParameter.AutovacuumMultixactFreezeMaxAge => "autovacuum_multixact_freeze_max_age",
        PostgresStorageParameter.AutovacuumMultixactFreezeTableAge => "autovacuum_multixact_freeze_table_age",
        PostgresStorageParameter.LogAutovacuumMinDuration => "log_autovacuum_min_duration",
        PostgresStorageParameter.UserCatalogTable => "user_catalog_table",
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null)
    };
}
