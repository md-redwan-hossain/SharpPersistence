namespace SharpPersistence.Migrator.Builders;

public sealed class CheckConstraintBuilder<TEntity> where TEntity : class
{
    internal CheckConstraintDefinition Definition { get; }

    internal CheckConstraintBuilder(CheckConstraintDefinition definition)
    {
        Definition = definition;
    }

    public CheckConstraintBuilder<TEntity> UseDelimitedIdentifiers(bool delimit = true)
    {
        Definition.DelimitIdentifiers = delimit;
        return this;
    }

    public CheckConstraintBuilder<TEntity> UseNestedColumnSeparator(string separator)
    {
        ArgumentNullException.ThrowIfNull(separator);
        Definition.NestedColumnSeparator = separator;
        return this;
    }
}
