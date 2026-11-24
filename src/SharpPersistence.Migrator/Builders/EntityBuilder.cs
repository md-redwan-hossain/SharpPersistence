using System.Linq.Expressions;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.Builders;

public sealed class EntityBuilder<TEntity> where TEntity : class
{
    internal TableDefinition Definition { get; }

    internal EntityBuilder(TableDefinition tableBuilder)
    {
        Definition = tableBuilder;
    }

    public EntityBuilder<TEntity> ToTable(string tableName, string? schema = null)
    {
        Definition.TableName = tableName;
        if (schema is not null)
        {
            Definition.Schema = schema;
        }

        return this;
    }

    public EntityBuilder<TEntity> HasPreviousTableName(string previousTableName, string? previousSchema = null)
    {
        Definition.PreviousTableName = previousTableName;
        Definition.PreviousSchema = previousSchema;
        return this;
    }

    public EntityBuilder<TEntity> HasComment(string? comment)
    {
        Definition.Comment = comment;
        return this;
    }

    public EntityBuilder<TEntity> Ignore(Expression<Func<TEntity, object?>> propertyExpression)
    {
        var propNames = ExpressionHelper.ExtractPropertyNames(propertyExpression);
        foreach (var propName in propNames)
        {
            Definition.IgnoreProperty(propName);
        }

        return this;
    }

    public EntityBuilder<TEntity> HasQueryFilter(Expression<Func<TEntity, bool>>? filter)
    {
        Definition.QueryFilter = filter;
        return this;
    }

    public EntityBuilder<TEntity> HasQueryFilter(LambdaExpression? filter)
    {
        Definition.QueryFilter = filter;
        return this;
    }

    public EntityBuilder<TEntity> HasQueryFilter(string filterKey, Expression<Func<TEntity, bool>>? filter)
    {
        Definition.NamedQueryFilters[filterKey] = filter;
        return this;
    }

    public EntityBuilder<TEntity> HasQueryFilter(string filterKey, LambdaExpression? filter)
    {
        Definition.NamedQueryFilters[filterKey] = filter;
        return this;
    }

    public KeyBuilder<TEntity> HasKey(Expression<Func<TEntity, object?>> keyExpression)
    {
        var propNames = ExpressionHelper.ExtractPropertyNames(keyExpression);
        Definition.SetPrimaryKey(propNames);
        return new KeyBuilder<TEntity>(Definition);
    }

    public ColumnBuilder<TEntity> Property<TProperty>(Expression<Func<TEntity, TProperty>> propertyExpression)
    {
        var propName = ExpressionHelper.ExtractSinglePropertyName(propertyExpression);
        return new ColumnBuilder<TEntity>(Definition.GetOrAddColumn(propName, typeof(TProperty)));
    }

    public IndexBuilder<TEntity, TKey> HasIndex<TKey>(Expression<Func<TEntity, TKey>> indexExpression)
    {
        var propNames = ExpressionHelper.ExtractPropertyNames(indexExpression);
        return new IndexBuilder<TEntity, TKey>(Definition.AddIndex(propNames));
    }

    public EntityBuilder<TEntity> HasCheckConstraint(string constraintName, string sqlExpression)
    {
        Definition.AddCheckConstraint(constraintName, sqlExpression);
        return this;
    }

    public CheckConstraintBuilder<TEntity> HasCheckConstraint(
        string constraintName,
        Func<SqlCheckConstraintBuilder<TEntity>, SqlPredicate> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraintName);
        ArgumentNullException.ThrowIfNull(predicate);

        var def = Definition.AddCheckConstraint(
            constraintName,
            (dialect, identifierCase, delimitIdentifiers, nestedSeparator) =>
            {
                var builder = new SqlCheckConstraintBuilder<TEntity>
                {
                    Dialect = dialect,
                    IdentifierCase = identifierCase,
                    DelimitIdentifiers = delimitIdentifiers,
                    NestedSeparator = nestedSeparator
                };

                var result = predicate(builder)
                    ?? throw new InvalidOperationException(
                        $"Check constraint '{constraintName}' on entity '{typeof(TEntity).Name}' returned null.");
                return result.ToString();
            });

        return new CheckConstraintBuilder<TEntity>(def);
    }

    public ForeignKeyReferenceBuilder<TEntity> HasForeignKey(
        Expression<Func<TEntity, object?>> dependentProperties)
    {
        var depProps = ExpressionHelper.ExtractPropertyNames(dependentProperties);
        return new ForeignKeyReferenceBuilder<TEntity>(Definition, depProps);
    }
}
