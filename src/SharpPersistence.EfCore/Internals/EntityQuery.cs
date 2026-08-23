using Microsoft.EntityFrameworkCore;
using SharpPersistence.Abstractions;

namespace SharpPersistence.EfCore.Internals;

internal sealed class EntityQuery<T> : EntityQueryBase<T>
    where T : class
{
    public EntityQuery(IQueryable<T> query) : base(query.AsNoTracking())
    {
    }

    public override IEntityQuery<T> EnableTracking()
    {
        Query = Query.AsTracking();
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