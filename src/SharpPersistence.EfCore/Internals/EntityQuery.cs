using Microsoft.EntityFrameworkCore;
using SharpPersistence.Abstractions;

namespace SharpPersistence.EfCore.Internals;

/// <summary>
/// Fluent query over mapped entities. EF tracking APIs require a class type, so this type
/// is constrained to <typeparamref name="T"/> : class. After <see cref="EntityQueryBase{T}.Select{TResult}"/>,
/// control moves to <see cref="EntityProjectedQuery{TResult}"/>, which supports any projection type.
/// </summary>
internal sealed class EntityQuery<T> : EntityQueryBase<T>
    where T : class
{
    public EntityQuery(IQueryable<T> query)
        : base(query.AsNoTracking())
    {
    }

    public override IEntityQuery<T> AsTracking()
    {
        Query = Query.AsTracking();
        return this;
    }

    public override IEntityQuery<T> AsNoTracking()
    {
        Query = Query.AsNoTracking();
        return this;
    }

#if NET10_0_OR_GREATER
    public override IEntityQuery<T> IgnoreQueryFilters(IReadOnlyCollection<string> filterKeys)
    {
        Query = Query.IgnoreQueryFilters(filterKeys);
        return this;
    }
#endif
}