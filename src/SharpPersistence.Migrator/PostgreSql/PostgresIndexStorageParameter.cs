namespace SharpPersistence.Migrator.PostgreSql;

public enum PostgresIndexStorageParameter : byte
{
    Fillfactor,
    DeduplicateItems,
    FastUpdate,
    GinPendingListLimit,
    Buffering,
    PagesPerRange,
    AutoSummarize
}

internal static class PostgresIndexStorageParameterExtensions
{
    public static string ToSqlName(this PostgresIndexStorageParameter parameter) => parameter switch
    {
        PostgresIndexStorageParameter.Fillfactor => "fillfactor",
        PostgresIndexStorageParameter.DeduplicateItems => "deduplicate_items",
        PostgresIndexStorageParameter.FastUpdate => "fastupdate",
        PostgresIndexStorageParameter.GinPendingListLimit => "gin_pending_list_limit",
        PostgresIndexStorageParameter.Buffering => "buffering",
        PostgresIndexStorageParameter.PagesPerRange => "pages_per_range",
        PostgresIndexStorageParameter.AutoSummarize => "autosummarize",
        _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null)
    };
}
