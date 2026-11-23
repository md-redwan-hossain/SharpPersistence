using SharpPersistence.Migrator.Abstractions;

namespace SharpPersistence.Migrator.Builders;

internal sealed class EntityRegistryBuilder : IEntityRegistryBuilder
{
    public string? DefaultSchema { get; private set; }
    public Dictionary<Type, Type?> Mappings { get; } = [];
    public Dictionary<Type, List<Action<object>>> InlineConfigurations { get; } = [];
    public List<SequenceDefinition> Sequences { get; } = [];

    public IEntityRegistryBuilder HasDefaultSchema(string schema)
    {
        DefaultSchema = schema;
        return this;
    }

    public IEntityRegistryBuilder Map<TEntity>() where TEntity : class
    {
        Mappings.TryAdd(typeof(TEntity), null);
        return this;
    }

    public IEntityRegistryBuilder Map<TEntity, TConfig>()
        where TEntity : class
        where TConfig : class, IEntityConfiguration<TEntity>, new()
    {
        Mappings[typeof(TEntity)] = typeof(TConfig);
        return this;
    }

    public IEntityRegistryBuilder Entity<TEntity>(Action<EntityBuilder<TEntity>> configure)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        Mappings.TryAdd(entityType, null);

        if (!InlineConfigurations.TryGetValue(entityType, out var actions))
        {
            actions = [];
            InlineConfigurations[entityType] = actions;
        }

        actions.Add(builderObj => configure((EntityBuilder<TEntity>)builderObj));
        return this;
    }

    public SequenceBuilder HasSequence<T>(string name, string? schema = null) =>
        HasSequence(typeof(T), name, schema);

    public SequenceBuilder HasSequence(Type clrType, string name, string? schema = null)
    {
        var seq = new SequenceDefinition(clrType, name, schema, schema is not null);
        Sequences.Add(seq);
        return new SequenceBuilder(seq);
    }

    internal static EntityRegistryBuilder Evaluate(IEntityRegistry registry)
    {
        var builder = new EntityRegistryBuilder();
        registry.Configure(builder);
        return builder;
    }
}
