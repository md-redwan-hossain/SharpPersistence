using System.Linq.Expressions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public sealed class IndexBuilder<TEntity, TKey> where TEntity : class
{
    internal IndexDefinition Definition { get; }

    internal IndexBuilder(IndexDefinition definition)
    {
        Definition = definition;
    }

    public IndexBuilder<TEntity, TKey> HasDatabaseName(string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        Definition.IndexName = indexName.Trim();
        return this;
    }

    public IndexBuilder<TEntity, TKey> IsUnique(bool unique = true)
    {
        Definition.IsUnique = unique;
        return this;
    }

    public IndexBuilder<TEntity, TKey> HasFilter(string? filterSql)
    {
        Definition.FilterSql = string.IsNullOrWhiteSpace(filterSql) ? null : filterSql.Trim();
        Definition.FilterRenderer = null;
        return this;
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

    public IndexBuilder<TEntity, TKey> IsDescending(params bool[] descending)
    {
        Definition.IsDescending = descending.Length == 0 ? [true] : [.. descending];
        return this;
    }

    public IndexBuilder<TEntity, TKey> HasMethod(string? method)
    {
        Definition.Method = method;
        return this;
    }
}
