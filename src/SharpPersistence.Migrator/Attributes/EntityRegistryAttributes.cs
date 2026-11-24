using System.ComponentModel;
using SharpPersistence.Abstractions;

namespace SharpPersistence.Migrator.Attributes;

/// <summary>
/// Marks a <c>sealed partial</c> <see cref="Abstractions.IEntityRegistry"/> class for compile-time entity discovery
/// using the built-in <see cref="EntityAttribute"/> marker.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityRegistryAttribute : Attribute;

/// <summary>
/// Marks a <c>sealed partial</c> <see cref="Abstractions.IEntityRegistry"/> class for compile-time entity discovery
/// targeting entities marked with <typeparamref name="TMarkerAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityRegistryAttribute<TMarkerAttribute> : Attribute
    where TMarkerAttribute : EntityMarkerAttribute;

/// <summary>
/// Marks a <c>sealed</c> <see cref="Abstractions.IEntityConfiguration{TEntity}"/> class for compile-time pairing
/// with <typeparamref name="TEntity"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityConfigurationAttribute<TEntity> : Attribute
    where TEntity : class;

/// <summary>
/// Marks the startup/root assembly so the generator emits <c>AddGeneratedEntityRegistries()</c>
/// aggregating all discovered <see cref="Abstractions.IEntityRegistry"/> classes across referenced assemblies.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class EntityRegistryRootAttribute : Attribute;

/// <summary>
/// Marks a compiler-generated registry aggregator class for O(1) cross-assembly root discovery.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityRegistryAggregatorAttribute : Attribute;

/// <summary>
/// Records discovered entity registry types on a generated registry aggregator class.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityRegistryAggregatorEntryAttribute(params Type[] registryTypes) : Attribute
{
    public Type[] RegistryTypes { get; } = registryTypes;
}
