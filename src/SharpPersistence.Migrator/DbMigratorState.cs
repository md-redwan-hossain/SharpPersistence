namespace SharpPersistence.Migrator;

/// <summary>
/// Ambient execution state for <see cref="Abstractions.IDbMigrator"/>.
/// </summary>
public static class DbMigratorState
{
    internal const string DesignTimeDataKey = "SharpPersistence.Migrator.IsDesignTime";

    /// <summary>
    /// Returns <see langword="true"/> when the application is being bootstrapped by the
    /// <c>db-migrator</c> CLI at design time (e.g. during <c>generate</c>, <c>doctor</c>, <c>ui</c>, or <c>status</c>).
    /// </summary>
    public static bool IsDesignTime =>
        AppContext.GetData(DesignTimeDataKey) is true;
}
