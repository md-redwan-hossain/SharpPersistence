using System.Linq.Expressions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public abstract class IndexBuilderBase<TEntity, TBuilder>
    where TEntity : class
    where TBuilder : IndexBuilderBase<TEntity, TBuilder>
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

    public TBuilder HasFilter(
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
        return Self;
    }

    public TBuilder IncludeProperties(Expression<Func<TEntity, object?>> includeExpression)
    {
        Definition.IncludedPropertyNames = [.. ExpressionHelper.ExtractPropertyNames(includeExpression)];
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

public sealed class IndexBuilder<TEntity> : IndexBuilderBase<TEntity, IndexBuilder<TEntity>>
    where TEntity : class
{
    internal IndexBuilder(IndexDefinition definition) : base(definition)
    {
    }
}

public sealed class IndexBuilder<TEntity, TKey> : IndexBuilderBase<TEntity, IndexBuilder<TEntity, TKey>>
    where TEntity : class
{
    internal IndexBuilder(IndexDefinition definition) : base(definition)
    {
    }
}
