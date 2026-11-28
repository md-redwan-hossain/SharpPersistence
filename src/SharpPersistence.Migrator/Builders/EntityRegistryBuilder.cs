using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Configuration;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Builders;

internal sealed class EntityRegistryBuilder : IEntityRegistryBuilder
{
    public string? DefaultSchema { get; private set; }
    public Dictionary<Type, Type?> Mappings { get; } = [];
    public List<PropertyConventionRule> PropertyConventions { get; } = [];
    public List<EntityConventionRule> EntityConventions { get; } = [];
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

    public IEntityRegistryBuilder ConfigureProperties<TProperty>(Action<ColumnBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        PropertyConventions.Add(new PropertyConventionRule(typeof(TProperty), configure));
        return this;
    }

    public IEntityRegistryBuilder ConfigureEntities(
        ModelCreationPhase phase,
        Action<EntityBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        EntityConventions.Add(new EntityConventionRule(
            phase,
            tableDef => configure(new EntityBuilder(tableDef))));
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
