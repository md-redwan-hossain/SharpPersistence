using SharpPersistence.Migrator.Builders;

namespace SharpPersistence.Migrator.Abstractions;

public interface IEntityConfiguration<TEntity> where TEntity : class
{
    void Configure(EntityBuilder<TEntity> builder);
}
