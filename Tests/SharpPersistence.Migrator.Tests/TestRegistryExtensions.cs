using SharpPersistence.Migrator.Abstractions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Enums;

namespace SharpPersistence.Migrator.Tests;

internal static class TestRegistryExtensions
{
    public static IEntityRegistryBuilder Entity<TEntity>(
        this IEntityRegistryBuilder registry,
        Action<EntityBuilder<TEntity>> configure)
        where TEntity : class
    {
        registry.Map<TEntity>();
        registry.ConfigureEntities(ModelCreationPhase.BeforeEntityConfigClass, m => m.Entity(configure));
        return registry;
    }
}
