using System.Linq.Expressions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public abstract class IndexBuilderBase<TBuilder>
    where TBuilder : IndexBuilderBase<TBuilder>
{
    internal IndexDefinition Definition { get; }

    internal IndexBuilderBase(IndexDefinition definition)
    {
        Definition = definition;
    }

    private TBuilder Self => (TBuilder)this;

    public TBuilder HasDatabaseName(string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        Definition.IndexName = indexName.Trim();
        return Self;
    }

    public TBuilder IsUnique(bool unique = true)
    {
        Definition.IsUnique = unique;
        return Self;
    }

    public TBuilder HasFilter(string? filterSql)
    {
        Definition.FilterSql = string.IsNullOrWhiteSpace(filterSql) ? null : filterSql.Trim();
        Definition.FilterRenderer = null;
        return Self;
    }

    public TBuilder IsDescending(params bool[] descending)
    {
        Definition.IsDescending = descending.Length == 0 ? [true] : [.. descending];
        return Self;
    }

    public TBuilder HasMethod(string? method)
    {
        Definition.Method = method;
        return Self;
    }
}

public sealed class IndexBuilder : IndexBuilderBase<IndexBuilder>
{
    internal IndexBuilder(IndexDefinition definition) : base(definition)
    {
    }

    public IndexBuilder IncludeProperties(params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);
        Definition.IncludedPropertyNames = [.. propertyNames];
        return this;
    }
}

public sealed class IndexBuilder<TEntity, TKey> : IndexBuilderBase<IndexBuilder<TEntity, TKey>> where TEntity : class
{
    internal IndexBuilder(IndexDefinition definition) : base(definition)
    {
    }

    public IndexBuilder<TEntity, TKey> HasFilter(
        Func<ISqlPredicateBuilder<TEntity>, SqlPredicate> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Definition.FilterSql = null;
        Definition.FilterRenderer = (dialect, identifierCase, delimitIdentifiers, nestedSeparator, resolveColumnName) =>
        {
            ISqlPredicateBuilder<TEntity> builder = SqlExpressionBuilderFactory.Create<TEntity>(
                dialect,
                identifierCase,
                delimitIdentifiers,
                nestedSeparator,
                resolveColumnName);

            var result = predicate(builder)
                ?? throw new InvalidOperationException(
                    $"Index filter on entity '{typeof(TEntity).Name}' returned null.");
            return result.ToString();
        };
        return this;
    }

    public IndexBuilder<TEntity, TKey> IncludeProperties(Expression<Func<TEntity, object?>> includeExpression)
    {
        Definition.IncludedPropertyNames = [.. ExpressionHelper.ExtractPropertyNames(includeExpression)];
        return this;
    }
}
