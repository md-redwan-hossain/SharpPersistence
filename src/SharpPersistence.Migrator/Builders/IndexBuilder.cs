using System.Linq.Expressions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public class IndexBuilder<TEntity> where TEntity : class
{
    internal IndexDefinition Definition { get; }

    internal IndexBuilder(IndexDefinition definition)
    {
        Definition = definition;
    }

    public IndexBuilder<TEntity> HasName(string indexName)
    {
        Definition.IndexName = indexName;
        return this;
    }

    public IndexBuilder<TEntity> IsUnique(bool unique = true)
    {
        Definition.IsUnique = unique;
        return this;
    }

    public IndexBuilder<TEntity> HasFilter(string? filterSql)
    {
        Definition.FilterSql = filterSql;
        return this;
    }

    public IndexBuilder<TEntity> IncludeProperties(Expression<Func<TEntity, object?>> includeExpression)
    {
        Definition.IncludedPropertyNames = [.. ExpressionHelper.ExtractPropertyNames(includeExpression)];
        return this;
    }

    public IndexBuilder<TEntity> IsDescending(params bool[] descending)
    {
        Definition.IsDescending = descending.Length == 0 ? [true] : [.. descending];
        return this;
    }

    public IndexBuilder<TEntity> HasMethod(string? method)
    {
        Definition.Method = method;
        return this;
    }
}

public sealed class IndexBuilder<TEntity, TKey> : IndexBuilder<TEntity> where TEntity : class
{
    internal IndexBuilder(IndexDefinition definition) : base(definition)
    {
    }

    public new IndexBuilder<TEntity, TKey> HasName(string indexName)
    {
        base.HasName(indexName);
        return this;
    }

    public new IndexBuilder<TEntity, TKey> IsUnique(bool unique = true)
    {
        base.IsUnique(unique);
        return this;
    }

    public new IndexBuilder<TEntity, TKey> HasFilter(string? filterSql)
    {
        base.HasFilter(filterSql);
        return this;
    }

    public new IndexBuilder<TEntity, TKey> IncludeProperties(Expression<Func<TEntity, object?>> includeExpression)
    {
        base.IncludeProperties(includeExpression);
        return this;
    }

    public new IndexBuilder<TEntity, TKey> IsDescending(params bool[] descending)
    {
        base.IsDescending(descending);
        return this;
    }

    public new IndexBuilder<TEntity, TKey> HasMethod(string? method)
    {
        base.HasMethod(method);
        return this;
    }
}
