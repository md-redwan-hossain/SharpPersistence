namespace SharpPersistence.Migrator;

/// <summary>
/// Publicly exposed convention constants and default paths used by SharpPersistence.Migrator.
/// </summary>
public static class DbMigratorConsts
{
    /// <summary>
    /// Name of the migration history table created in the target database.
    /// </summary>
    public const string HistoryTableName = "__db_migrator_logs";

    /// <summary>
    /// Default base directory for migration artifacts relative to the project root.
    /// </summary>
    public const string BaseDirectory = "DbMigrator";

    /// <summary>
    /// Default subdirectory name for generated SQL migration scripts under <see cref="BaseDirectory"/>.
    /// </summary>
    public const string ScriptsDirectoryName = "Scripts";

    /// <summary>
    /// Default subdirectory name for schema snapshots under <see cref="BaseDirectory"/>.
    /// </summary>
    public const string SnapshotsDirectoryName = "Snapshots";

    /// <summary>
    /// File name of the primary migration execution runlist JSON stored in the scripts directory.
    /// </summary>
    public const string RunlistFileName = "db-migration-runlist.json";

    /// <summary>
    /// File name of the optional user-maintained runlist overrides JSON stored in the scripts directory.
    /// </summary>
    public const string RunlistOverridesFileName = "db-migration-runlist.overrides.json";

    /// <summary>
    /// Default relative path to the generated SQL migration scripts directory (<c>DbMigrator/Scripts</c>).
    /// </summary>
    public static readonly string ScriptsDirectory = Path.Combine(BaseDirectory, ScriptsDirectoryName);

    /// <summary>
    /// Default relative path to the schema snapshots directory (<c>DbMigrator/Snapshots</c>).
    /// </summary>
    public static readonly string SnapshotsDirectory = Path.Combine(BaseDirectory, SnapshotsDirectoryName);
}
