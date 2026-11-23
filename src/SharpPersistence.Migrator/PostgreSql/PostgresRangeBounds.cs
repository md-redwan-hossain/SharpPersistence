namespace SharpPersistence.Migrator.PostgreSql;

public enum PostgresRangeBounds
{
    /// <summary>Half-open range <c>'[)'</c>: inclusive lower bound, exclusive upper bound (PostgreSQL default).</summary>
    InclusiveLowerExclusiveUpper,

    /// <summary>Closed range <c>'[]'</c>: both lower and upper bounds inclusive.</summary>
    Inclusive,

    /// <summary>Open range <c>'()'</c>: both lower and upper bounds exclusive.</summary>
    Exclusive,

    /// <summary>Half-open range <c>'(]'</c>: exclusive lower bound, inclusive upper bound.</summary>
    ExclusiveLowerInclusiveUpper
}

internal static class PostgresRangeBoundsExtensions
{
    public static string ToSqlBounds(this PostgresRangeBounds bounds) => bounds switch
    {
        PostgresRangeBounds.InclusiveLowerExclusiveUpper => "[)",
        PostgresRangeBounds.Inclusive => "[]",
        PostgresRangeBounds.Exclusive => "()",
        PostgresRangeBounds.ExclusiveLowerInclusiveUpper => "(]",
        _ => throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "Unsupported PostgreSQL range bounds.")
    };
}
