using System.Globalization;
using System.Linq.Expressions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.PostgreSql;

public static class PostgreSqlIndexBuilderExtensions
{
    public static TBuilder HasMethod<TBuilder>(
        this IndexBuilderBase<TBuilder> builder,
        PostgresIndexMethod method)
        where TBuilder : IndexBuilderBase<TBuilder> =>
        builder.HasMethod(method.ToSqlName());

    public static TBuilder HasOperator<TBuilder>(
        this IndexBuilderBase<TBuilder> builder,
        string operatorClass)
        where TBuilder : IndexBuilderBase<TBuilder>
    {
        if (builder.Definition.PropertyNames.Count != 1)
        {
            throw new InvalidOperationException(
                "Use HasOperator(k => k.Property, operatorClass) or HasOperator(propertyName, operatorClass) when configuring operators on a composite index.");
        }

        builder.Definition.OperatorsByProperty ??= new Dictionary<string, string>(StringComparer.Ordinal);
        builder.Definition.OperatorsByProperty[builder.Definition.PropertyNames[0]] = operatorClass;
        return (TBuilder)builder;
    }

    public static IndexBuilder HasOperator(
        this IndexBuilder builder,
        string propertyName,
        string operatorClass)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (!builder.Definition.PropertyNames.Contains(propertyName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Property '{propertyName}' is not part of the index columns ({string.Join(", ", builder.Definition.PropertyNames)}).");
        }

        builder.Definition.OperatorsByProperty ??= new Dictionary<string, string>(StringComparer.Ordinal);
        builder.Definition.OperatorsByProperty[propertyName] = operatorClass;
        return builder;
    }

    public static IndexBuilder<TEntity, TKey> HasOperator<TEntity, TKey>(
        this IndexBuilder<TEntity, TKey> builder,
        Expression<Func<TKey, object?>> keyPropertyExpression,
        string operatorClass)
        where TEntity : class
    {
        var propName = ExpressionHelper.ExtractSinglePropertyName(keyPropertyExpression);
        if (!builder.Definition.PropertyNames.Contains(propName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Property '{propName}' is not part of the index columns ({string.Join(", ", builder.Definition.PropertyNames)}).");
        }

        builder.Definition.OperatorsByProperty ??= new Dictionary<string, string>(StringComparer.Ordinal);
        builder.Definition.OperatorsByProperty[propName] = operatorClass;
        return builder;
    }

    public static TBuilder UseTablespace<TBuilder>(
        this IndexBuilderBase<TBuilder> builder,
        string? tablespace)
        where TBuilder : IndexBuilderBase<TBuilder>
    {
        builder.Definition.Tablespace = string.IsNullOrWhiteSpace(tablespace) ? null : tablespace.Trim();
        return (TBuilder)builder;
    }

    public static TBuilder HasStorageParameter<TBuilder>(
        this IndexBuilderBase<TBuilder> builder,
        PostgresIndexStorageParameter parameter,
        object value)
        where TBuilder : IndexBuilderBase<TBuilder> =>
        builder.HasStorageParameter(parameter.ToSqlName(), value);

    public static TBuilder HasStorageParameter<TBuilder>(
        this IndexBuilderBase<TBuilder> builder,
        string name,
        object value)
        where TBuilder : IndexBuilderBase<TBuilder>
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

        builder.Definition.StorageParameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        builder.Definition.StorageParameters[name.Trim()] = formatted;
        return (TBuilder)builder;
    }
}
