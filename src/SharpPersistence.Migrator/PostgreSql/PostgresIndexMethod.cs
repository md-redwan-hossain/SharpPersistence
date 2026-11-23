namespace SharpPersistence.Migrator.PostgreSql;

public enum PostgresIndexMethod : byte
{
    BTree,
    Hash,
    Gin,
    Gist,
    SpGist,
    Brin
}

internal static class PostgresIndexMethodExtensions
{
    public static string ToSqlName(this PostgresIndexMethod method) => method switch
    {
        PostgresIndexMethod.BTree => "btree",
        PostgresIndexMethod.Hash => "hash",
        PostgresIndexMethod.Gin => "gin",
        PostgresIndexMethod.Gist => "gist",
        PostgresIndexMethod.SpGist => "spgist",
        PostgresIndexMethod.Brin => "brin",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
    };
}
