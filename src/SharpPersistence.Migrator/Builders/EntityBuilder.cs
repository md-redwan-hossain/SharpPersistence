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
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        Definition.TableName = tableName.Trim();
        if (schema is not null)
        {
            Definition.Schema = string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
        }

        return this;
    }

    public EntityBuilder<TEntity> HasSchema(string? schema)
    {
        Definition.Schema = string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
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

    public EntityBuilder<TEntity> ExcludeFromMigrations(bool excluded = true)
    {
        Definition.ExcludeFromMigrations = excluded;
        return this;
    }

    public EntityBuilder<TEntity> SetAnnotation<TValue>(string name, TValue? value)
    {
        Definition.SetAnnotation(name, value);
        return this;
    }

    public bool HasAnnotation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Definition.Annotations.ContainsKey(name);
    }

    public TValue? GetAnnotation<TValue>(string name, TValue? defaultValue = default) =>
        TryGetAnnotation<TValue>(name, out var value) ? value : defaultValue;

    public bool TryGetAnnotation<TValue>(string name, out TValue? value) =>
        EntityBuilder.TryGetAnnotationCore(Definition.Annotations, name, out value);

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

    public EntityBuilder<TEntity> HasQueryFilter(string filterKey, Expression<Func<TEntity, bool>>? filter)
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
        Func<ISqlPredicateBuilder<TEntity>, SqlPredicate> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraintName);
        ArgumentNullException.ThrowIfNull(predicate);

        var def = Definition.AddCheckConstraint(
            constraintName,
            (dialect, identifierCase, delimitIdentifiers, nestedSeparator, resolveColumnName) =>
            {
                ISqlPredicateBuilder<TEntity> builder = SqlExpressionBuilderFactory.Create<TEntity>(
                    dialect,
                    identifierCase,
                    delimitIdentifiers,
                    nestedSeparator,
                    resolveColumnName);

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

    public EntityBuilder<TEntity> OwnsOne<TOwned>(
        Expression<Func<TEntity, TOwned?>> navigationExpression,
        Action<OwnedEntityBuilder<TEntity, TOwned>>? configure = null)
        where TOwned : class
    {
        ArgumentNullException.ThrowIfNull(navigationExpression);
        var navPropName = ExpressionHelper.ExtractSinglePropertyName(navigationExpression);
        var navDef = Definition.GetOrAddOwnedNavigation(
            parentType: typeof(TEntity),
            navigationPropertyName: navPropName,
            ownedClrType: typeof(TOwned),
            parentNavigation: null);

        configure?.Invoke(new OwnedEntityBuilder<TEntity, TOwned>(Definition, navDef));
        return this;
    }
}

public sealed class EntityBuilder
{
    internal TableDefinition Definition { get; }

    internal EntityBuilder(TableDefinition tableBuilder)
    {
        Definition = tableBuilder;
    }

    public Type ClrType => Definition.EntityType;
    public string TableName => Definition.TableName;
    public string? Schema => Definition.Schema;

    public EntityBuilder Entity<TEntity>(Action<EntityBuilder<TEntity>> configure)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (typeof(TEntity).IsAssignableFrom(Definition.EntityType))
        {
            configure(new EntityBuilder<TEntity>(Definition));
        }

        return this;
    }

    public IReadOnlyList<ColumnBuilder> Properties =>
        Definition.ColumnsByProp.Values
            .Where(c => !Definition.IgnoredProperties.Contains(c.PropertyName))
            .Select(c => new ColumnBuilder(c))
            .ToList();

    public ColumnBuilder? FindProperty(string propertyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (Definition.IgnoredProperties.Contains(propertyName))
        {
            return null;
        }

        return Definition.ColumnsByProp.TryGetValue(propertyName, out var col)
            ? new ColumnBuilder(col)
            : null;
    }

    public ColumnBuilder Property(string propertyName) =>
        FindProperty(propertyName)
        ?? throw new InvalidOperationException(
            $"Property '{propertyName}' is not mapped on entity '{Definition.EntityType.Name}'.");

    public EntityBuilder ToTable(string tableName, string? schema = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        Definition.TableName = tableName.Trim();
        if (schema is not null)
        {
            Definition.Schema = string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
        }

        return this;
    }

    public EntityBuilder HasSchema(string? schema)
    {
        Definition.Schema = string.IsNullOrWhiteSpace(schema) ? null : schema.Trim();
        return this;
    }

    public EntityBuilder HasComment(string? comment)
    {
        Definition.Comment = comment;
        return this;
    }

    public EntityBuilder ExcludeFromMigrations(bool excluded = true)
    {
        Definition.ExcludeFromMigrations = excluded;
        return this;
    }

    public IndexBuilder HasIndex(params string[] propertyNames)
    {
        ArgumentNullException.ThrowIfNull(propertyNames);
        if (propertyNames.Length == 0)
        {
            throw new ArgumentException("At least one property name is required.", nameof(propertyNames));
        }

        return new IndexBuilder(Definition.AddIndex(propertyNames));
    }

    public EntityBuilder SetAnnotation<TValue>(string name, TValue? value)
    {
        Definition.SetAnnotation(name, value);
        return this;
    }

    public bool HasAnnotation(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Definition.Annotations.ContainsKey(name);
    }

    public TValue? GetAnnotation<TValue>(string name, TValue? defaultValue = default) =>
        TryGetAnnotation<TValue>(name, out var value) ? value : defaultValue;

    public bool TryGetAnnotation<TValue>(string name, out TValue? value) =>
        TryGetAnnotationCore(Definition.Annotations, name, out value);

    internal static bool TryGetAnnotationCore<TValue>(
        IReadOnlyDictionary<string, object?> annotations,
        string name,
        out TValue? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (annotations.TryGetValue(name, out var raw))
        {
            if (raw is TValue typed)
            {
                value = typed;
                return true;
            }

            if (raw is null && default(TValue) is null)
            {
                value = default;
                return true;
            }
        }

        value = default;
        return false;
    }
}
