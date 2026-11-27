namespace SharpPersistence.Migrator.Configuration;

public sealed class EntityTypeReplacementBuilder
{
    private readonly Dictionary<Type, Type> _replacements;

    internal EntityTypeReplacementBuilder(Dictionary<Type, Type> replacements)
    {
        _replacements = replacements;
    }

    public EntityTypeReplacementBuilder Replace<TFrom, TTo>()
        where TFrom : class
        where TTo : class
    {
        if (typeof(TFrom) == typeof(TTo))
        {
            throw new ArgumentException(
                $"Entity type '{typeof(TFrom).Name}' cannot be replaced with itself.",
                nameof(TTo));
        }

        _replacements[typeof(TFrom)] = typeof(TTo);
        return this;
    }
}
