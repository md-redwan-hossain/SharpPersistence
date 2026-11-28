using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Abstractions;

public interface IEntityRegistryBuilder
{
    IEntityRegistryBuilder HasDefaultSchema(string schema);

    IEntityRegistryBuilder Map<TEntity>() where TEntity : class;

    IEntityRegistryBuilder Map<TEntity, TConfig>()
        where TEntity : class
        where TConfig : class, IEntityConfiguration<TEntity>, new();

    IEntityRegistryBuilder ConfigureProperties<TProperty>(Action<ColumnBuilder> configure);

    IEntityRegistryBuilder ConfigureEntities(
        ModelCreationPhase phase,
        Action<EntityBuilder> configure);

    SequenceBuilder HasSequence<T>(string name, string? schema = null);

    SequenceBuilder HasSequence(Type clrType, string name, string? schema = null);
}
