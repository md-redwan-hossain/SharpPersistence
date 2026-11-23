namespace SharpPersistence.Migrator.PostgreSql;

public enum PostgresExclusionOperator
{
    Equals,
    NotEquals,
    Overlaps,
    Contains,
    ContainedBy,
    Adjacent,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}

internal static class PostgresExclusionOperatorExtensions
{
    public static string ToSqlOperator(this PostgresExclusionOperator op) => op switch
    {
        PostgresExclusionOperator.Equals => "=",
        PostgresExclusionOperator.NotEquals => "<>",
        PostgresExclusionOperator.Overlaps => "&&",
        PostgresExclusionOperator.Contains => "@>",
        PostgresExclusionOperator.ContainedBy => "<@",
        PostgresExclusionOperator.Adjacent => "-|-",
        PostgresExclusionOperator.LessThan => "<",
        PostgresExclusionOperator.LessThanOrEqual => "<=",
        PostgresExclusionOperator.GreaterThan => ">",
        PostgresExclusionOperator.GreaterThanOrEqual => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unsupported PostgreSQL exclusion operator.")
    };
}
