using SharpPersistence.Migrator.Builders;

namespace SharpPersistence.Migrator.Abstractions;

public interface IEntityRegistryBuilder
{
    IEntityRegistryBuilder HasDefaultSchema(string schema);

    IEntityRegistryBuilder Map<TEntity>() where TEntity : class;

    IEntityRegistryBuilder Map<TEntity, TConfig>()
        where TEntity : class
        where TConfig : class, IEntityConfiguration<TEntity>, new();

    IEntityRegistryBuilder Entity<TEntity>(Action<EntityBuilder<TEntity>> configure)
        where TEntity : class;

    SequenceBuilder HasSequence<T>(string name, string? schema = null);

    SequenceBuilder HasSequence(Type clrType, string name, string? schema = null);
}
