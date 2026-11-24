namespace SharpPersistence.Migrator.Configuration;

public sealed class EntityStubBuilder
{
    private readonly Dictionary<Type, Type> _mappings;

    internal EntityStubBuilder(Dictionary<Type, Type> mappings)
    {
        _mappings = mappings;
    }

    public EntityStubBuilder Map<TStub, TEntity>()
        where TStub : class
        where TEntity : class
    {
        if (typeof(TStub) == typeof(TEntity))
        {
            throw new ArgumentException(
                $"Entity stub '{typeof(TStub).Name}' cannot be mapped to itself.",
                nameof(TEntity));
        }

        _mappings[typeof(TStub)] = typeof(TEntity);
        return this;
    }
}
