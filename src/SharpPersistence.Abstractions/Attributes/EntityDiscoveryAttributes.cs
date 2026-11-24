using System.ComponentModel;

namespace SharpPersistence.Abstractions;

/// <summary>
/// Base class for module-specific entity marker attributes used by compile-time entity discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public abstract class EntityMarkerAttribute : Attribute;

/// <summary>
/// Built-in default entity marker attribute for projects that do not define a custom per-module marker attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityAttribute : EntityMarkerAttribute;

/// <summary>
/// Registers a custom <see cref="EntityMarkerAttribute"/> subclass for compile-time entity manifest generation in this assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class DiscoverEntitiesByAttribute<TMarkerAttribute> : Attribute
    where TMarkerAttribute : EntityMarkerAttribute;

/// <summary>
/// Disables compile-time entity discovery and registry source generation in this assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class DisableEntityDiscoveryAttribute : Attribute;

/// <summary>
/// Marks a compiler-generated entity manifest class for O(1) cross-assembly lookup.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class EntityManifestAttribute : Attribute;

/// <summary>
/// Records discovered entities grouped by their marker attribute type on a generated entity manifest class.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class EntityManifestEntryAttribute(Type markerType, params Type[] entityTypes) : Attribute
{
    public Type MarkerType { get; } = markerType;
    public Type[] EntityTypes { get; } = entityTypes;
}
