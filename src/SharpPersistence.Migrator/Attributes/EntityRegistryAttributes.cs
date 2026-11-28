using System.ComponentModel;
using SharpPersistence.Abstractions;
using SharpPersistence.Abstractions.Attributes;

namespace SharpPersistence.Migrator.Attributes;

/// <summary>
/// Marks a <c>sealed partial</c> <see cref="Abstractions.IEntityRegistry"/> class for compile-time entity discovery
/// using the built-in <see cref="EntityAttribute"/> marker.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityRegistryAttribute : Attribute;

/// <summary>
/// Marks a <c>sealed partial</c> <see cref="Abstractions.IEntityRegistry"/> class for compile-time entity discovery
/// targeting entities marked with <see cref="EntityAttribute{TGroup}"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityRegistryAttribute<TGroup> : Attribute
    where TGroup : struct, IEntityGroup;

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
/// Records discovered entity registry types on a compiler-generated entity registry index class.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityRegistryIndexAttribute(params Type[] registryTypes) : Attribute
{
    public Type[] RegistryTypes { get; } = registryTypes;
}
