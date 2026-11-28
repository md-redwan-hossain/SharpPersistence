using System.ComponentModel;

namespace SharpPersistence.Abstractions.Attributes;

/// <summary>
/// Marker interface for value-type entity group tags used with <see cref="EntityAttribute{TGroup}"/>
/// (e.g., <c>public readonly record struct SalesGroup : IEntityGroup;</c>).
/// </summary>
public interface IEntityGroup;

/// <summary>
/// Internal metadata contract shared by <see cref="EntityAttribute"/> and <see cref="EntityAttribute{TGroup}"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IEntityAttributeMetadata
{
    bool ReadOnly { get; }
}

/// <summary>
/// Marks a class as a discovered entity belonging to the default (ungrouped) entity registry.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityAttribute : Attribute, IEntityAttributeMetadata
{
    /// <summary>
    /// Marks the entity as read-only (e.g., a cross-module stub or database view).
    /// Read-only entities are automatically excluded from migrations and generated as
    /// <c>IQueryable&lt;T&gt;</c> (<c>AsNoTracking</c>) on bound EF Core <c>DbContext</c> classes.
    /// </summary>
    public bool ReadOnly { get; set; }
}

/// <summary>
/// Marks a class as a discovered entity belonging to the <typeparamref name="TGroup"/> entity group.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityAttribute<TGroup> : Attribute, IEntityAttributeMetadata
    where TGroup : struct, IEntityGroup
{
    /// <summary>
    /// Marks the entity as read-only (e.g., a cross-module stub or database view).
    /// Read-only entities are automatically excluded from migrations and generated as
    /// <c>IQueryable&lt;T&gt;</c> (<c>AsNoTracking</c>) on bound EF Core <c>DbContext</c> classes.
    /// </summary>
    public bool ReadOnly { get; set; }
}

/// <summary>
/// Disables compile-time entity discovery and registry source generation in this assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class DisableEntityDiscoveryAttribute : Attribute;

/// <summary>
/// Records discovered entities grouped by their group type on a compiler-generated entity index class.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class EntityIndexAttribute(Type groupType, params Type[] entityTypes) : Attribute
{
    public Type GroupType { get; } = groupType;
    public Type[] EntityTypes { get; } = entityTypes;
}
