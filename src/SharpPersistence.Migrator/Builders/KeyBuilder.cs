namespace SharpPersistence.Migrator.Builders;

public sealed class KeyBuilder<TEntity> where TEntity : class
{
    private readonly TableDefinition _table;

    internal KeyBuilder(TableDefinition table)
    {
        _table = table;
    }

    public KeyBuilder<TEntity> HasName(string constraintName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraintName);
        _table.SetPrimaryKeyName(constraintName.Trim());
        return this;
    }
}
