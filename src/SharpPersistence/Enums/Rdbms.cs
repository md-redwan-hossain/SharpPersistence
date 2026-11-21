namespace SharpPersistence.Enums;

[Obsolete("Use SharpPersistence.Enums.SqlDialect instead.")]
public enum Rdbms : byte
{
    PostgreSql = 1,
    MySql = 2,
    SqlServer = 3,
    Oracle = 4
}