using SharpPersistence.Migrator.Abstractions;

namespace SharpPersistence.Migrator.EfCore.Attributes;

/// <summary>
/// Binds an <see cref="IEntityRegistry"/> to a <c>sealed partial</c> EF Core <c>DbContext</c>,
/// generating <c>DbSet&lt;T&gt;</c> / <c>IQueryable&lt;T&gt;</c> properties and optional
/// constructor / <c>OnModelCreating</c> wiring at compile time.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class BindEntityRegistryAttribute<TRegistry> : Attribute
    where TRegistry : IEntityRegistry
{
    /// <summary>
    /// When <c>true</c>, all entities from <typeparamref name="TRegistry"/> are exposed as
    /// <c>IQueryable&lt;T&gt;</c> with <c>AsNoTracking()</c> instead of <c>DbSet&lt;T&gt;</c>.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// When <c>true</c> (default), generated property names are pluralized (e.g., <c>Invoice</c> -&gt; <c>Invoices</c>).
    /// </summary>
    public bool Pluralize { get; set; } = true;
}

/// <summary>
/// Overrides how a specific entity from a bound <see cref="IEntityRegistry"/> is generated on a <c>DbContext</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class EntityRegistryItemAttribute<TEntity> : Attribute
    where TEntity : class
{
    /// <summary>
    /// Explicitly overrides whether <typeparamref name="TEntity"/> is generated as
    /// <c>IQueryable&lt;TEntity&gt;</c> (<c>true</c>) or <c>DbSet&lt;TEntity&gt;</c> (<c>false</c>).
    /// Cannot be combined with <see cref="Exclude"/> = <c>true</c>.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>
    /// Explicitly overrides whether the generated property name for <typeparamref name="TEntity"/> is pluralized.
    /// Cannot be combined with <see cref="Exclude"/> = <c>true</c>.
    /// </summary>
    public bool Pluralize { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, omits <typeparamref name="TEntity"/> from generated <c>DbContext</c> properties.
    /// </summary>
    public bool Exclude { get; set; }
}
