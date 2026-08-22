using SharpPersistence.Abstractions;

namespace SharpPersistence.EfCore.Internals;

/// <summary>
/// Fluent query after <see cref="EntityQuery{T}"/>.<see cref="EntityQueryBase{T}.Select{TResult}"/>.
/// Exists as a separate type because EF's <c>AsTracking</c>/<c>AsNoTracking</c> only accept class
/// entity types, while a projection can be a DTO, string, int, etc. Tracking must be configured on
/// <see cref="EntityQuery{T}"/> before Select; calling tracking methods here does nothing
/// (they return <c>this</c> unchanged so the chain still compiles against <see cref="IEntityQuery{T}"/>).
/// </summary>
internal sealed class EntityProjectedQuery<T> : EntityQueryBase<T>
{
    public EntityProjectedQuery(IQueryable<T> query)
        : base(query)
    {
    }

    /// <summary>
    /// Does nothing. Change tracking does not apply to projected results; set it on the entity query before Select.
    /// </summary>
    public override IEntityQuery<T> AsTracking() => this;

    /// <summary>
    /// Does nothing. Change tracking does not apply to projected results; set it on the entity query before Select.
    /// </summary>
    public override IEntityQuery<T> AsNoTracking() => this;

#if NET10_0_OR_GREATER
    /// <summary>
    /// Does nothing. Named query filters must be ignored on the entity query before Select.
    /// </summary>
    public override IEntityQuery<T> IgnoreQueryFilters(IReadOnlyCollection<string> filterKeys) => this;
#endif
}