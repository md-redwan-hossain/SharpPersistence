using System.Globalization;
using System.Linq.Expressions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.PostgreSql;

public sealed class PostgresExclusionConstraintBuilder<TEntity> where TEntity : class
{
    internal ExclusionConstraintDefinition Definition { get; }

    internal PostgresExclusionConstraintBuilder(ExclusionConstraintDefinition definition)
    {
        Definition = definition;
    }

    public PostgresExclusionConstraintBuilder<TEntity> HasMethod(PostgresIndexMethod method)
    {
        Definition.Method = method.ToSqlName();
        return this;
    }

    public PostgresExclusionConstraintBuilder<TEntity> WithElement<TProp>(
        Expression<Func<TEntity, TProp>> propertyExpression,
        PostgresExclusionOperator op,
        string? operatorClass = null) =>
        WithElement(propertyExpression, op.ToSqlOperator(), operatorClass);

    public PostgresExclusionConstraintBuilder<TEntity> WithElement<TProp>(
        Expression<Func<TEntity, TProp>> propertyExpression,
        string rawOperator,
        string? operatorClass = null)
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawOperator);

        var propName = ExpressionHelper.ExtractSinglePropertyName(propertyExpression);
        Definition.Elements.Add(new ExclusionConstraintElementDefinition(
            PropertyName: propName,
            ExpressionSql: null,
            OperatorClass: string.IsNullOrWhiteSpace(operatorClass) ? null : operatorClass.Trim(),
            Operator: rawOperator.Trim()));
        return this;
    }

    public PostgresExclusionConstraintBuilder<TEntity> WithExpression(
        Func<ISqlExpressionBuilder<TEntity>, SqlBuiltExpression> expression,
        PostgresExclusionOperator op) =>
        WithExpression(expression, op.ToSqlOperator());

    public PostgresExclusionConstraintBuilder<TEntity> WithExpression(
        Func<ISqlExpressionBuilder<TEntity>, SqlBuiltExpression> expression,
        string rawOperator)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawOperator);

        Definition.Elements.Add(new ExclusionConstraintElementDefinition(
            PropertyName: null,
            ExpressionSql: null,
            OperatorClass: null,
            Operator: rawOperator.Trim(),
            SqlExpressionRenderer: (dialect, identifierCase, delimitIdentifiers, nestedSeparator) =>
            {
                ISqlExpressionBuilder<TEntity> builder = new SqlExpressionBuilder<TEntity>
                {
                    Dialect = dialect,
                    IdentifierCase = identifierCase,
                    DelimitIdentifiers = delimitIdentifiers,
                    NestedSeparator = nestedSeparator
                };

                var result = expression(builder)
                    ?? throw new InvalidOperationException(
                        $"Exclusion constraint expression on '{Definition.Name}' returned null.");
                var sql = result.ToString();
                ArgumentException.ThrowIfNullOrWhiteSpace(sql);
                return sql.Trim();
            }));
        return this;
    }

    public PostgresExclusionConstraintBuilder<TEntity> WithRange<TProp>(
        Expression<Func<TEntity, TProp>> lowerProperty,
        Expression<Func<TEntity, TProp>> upperProperty,
        PostgresExclusionOperator op,
        PostgresRangeBounds bounds = PostgresRangeBounds.InclusiveLowerExclusiveUpper) =>
        WithRange(lowerProperty, upperProperty, op.ToSqlOperator(), bounds);

    public PostgresExclusionConstraintBuilder<TEntity> WithRange<TProp>(
        Expression<Func<TEntity, TProp>> lowerProperty,
        Expression<Func<TEntity, TProp>> upperProperty,
        string rawOperator,
        PostgresRangeBounds bounds = PostgresRangeBounds.InclusiveLowerExclusiveUpper)
    {
        ArgumentNullException.ThrowIfNull(lowerProperty);
        ArgumentNullException.ThrowIfNull(upperProperty);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawOperator);

        var lowerProp = ExpressionHelper.ExtractSinglePropertyName(lowerProperty);
        var upperProp = ExpressionHelper.ExtractSinglePropertyName(upperProperty);
        var rangeFunction = ResolveRangeFunction(typeof(TProp));
        var sqlBounds = bounds.ToSqlBounds();

        Definition.Elements.Add(new ExclusionConstraintElementDefinition(
            PropertyName: null,
            ExpressionSql: null,
            OperatorClass: null,
            Operator: rawOperator.Trim(),
            ExpressionRenderer: resolveQuotedColumn =>
                $"{rangeFunction}({resolveQuotedColumn(lowerProp)}, {resolveQuotedColumn(upperProp)}, '{sqlBounds}')"));
        return this;
    }

    private static string ResolveRangeFunction(Type propertyType)
    {
        var unwrapped = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (unwrapped == typeof(DateTimeOffset)) return "tstzrange";
        if (unwrapped == typeof(DateTime)) return "tsrange";
        if (unwrapped == typeof(DateOnly)) return "daterange";
        if (unwrapped == typeof(int)) return "int4range";
        if (unwrapped == typeof(long)) return "int8range";
        if (unwrapped == typeof(decimal)) return "numrange";

        throw new NotSupportedException(
            $"Type '{unwrapped.Name}' does not have a built-in PostgreSQL range constructor. Use WithExpression(...) instead.");
    }

    public PostgresExclusionConstraintBuilder<TEntity> IncludeProperties(
        Expression<Func<TEntity, object?>> includeExpression)
    {
        Definition.IncludedPropertyNames = [.. ExpressionHelper.ExtractPropertyNames(includeExpression)];
        return this;
    }

    public PostgresExclusionConstraintBuilder<TEntity> HasStorageParameter(
        PostgresIndexStorageParameter parameter,
        object value) =>
        HasStorageParameter(parameter.ToSqlName(), value);

    public PostgresExclusionConstraintBuilder<TEntity> HasStorageParameter(string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Index storage parameter name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(value);

        var formatted = value switch
        {
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        Definition.StorageParameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Definition.StorageParameters[name.Trim()] = formatted;
        return this;
    }

    public PostgresExclusionConstraintBuilder<TEntity> UseTablespace(string? tablespace)
    {
        Definition.Tablespace = string.IsNullOrWhiteSpace(tablespace) ? null : tablespace.Trim();
        return this;
    }

    public PostgresExclusionConstraintBuilder<TEntity> HasFilter(string? filterSql)
    {
        Definition.FilterSql = string.IsNullOrWhiteSpace(filterSql) ? null : filterSql.Trim();
        Definition.FilterRenderer = null;
        return this;
    }

    public PostgresExclusionConstraintBuilder<TEntity> HasFilter(
        Func<ISqlPredicateBuilder<TEntity>, SqlPredicate> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Definition.FilterSql = null;
        Definition.FilterRenderer = (dialect, identifierCase, delimitIdentifiers, nestedSeparator) =>
        {
            ISqlPredicateBuilder<TEntity> builder = new SqlExpressionBuilder<TEntity>
            {
                Dialect = dialect,
                IdentifierCase = identifierCase,
                DelimitIdentifiers = delimitIdentifiers,
                NestedSeparator = nestedSeparator
            };

            var result = predicate(builder)
                ?? throw new InvalidOperationException(
                    $"Exclusion constraint filter on '{Definition.Name}' returned null.");
            return result.ToString();
        };
        return this;
    }
}
