using System.Globalization;
using System.Linq.Expressions;
using SharpPersistence.Migrator.Builders;
using SharpPersistence.Migrator.Services;

namespace SharpPersistence.Migrator.PostgreSql;

public static class PostgreSqlIndexBuilderExtensions
{
    public static IndexBuilder<TEntity, TKey> HasMethod<TEntity, TKey>(
        this IndexBuilder<TEntity, TKey> builder,
        PostgresIndexMethod method)
        where TEntity : class =>
        builder.HasMethod(method.ToSqlName());

    public static IndexBuilder<TEntity, TKey> HasOperator<TEntity, TKey>(
        this IndexBuilder<TEntity, TKey> builder,
        string operatorClass)
        where TEntity : class
    {
        if (builder.Definition.PropertyNames.Count != 1)
        {
            throw new InvalidOperationException(
                "Use HasOperator(k => k.Property, operatorClass) when configuring operators on a composite index.");
        }

        builder.Definition.OperatorsByProperty ??= new Dictionary<string, string>(StringComparer.Ordinal);
        builder.Definition.OperatorsByProperty[builder.Definition.PropertyNames[0]] = operatorClass;
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

    public static IndexBuilder<TEntity, TKey> UseTablespace<TEntity, TKey>(
        this IndexBuilder<TEntity, TKey> builder,
        string? tablespace)
        where TEntity : class
    {
        builder.Definition.Tablespace = string.IsNullOrWhiteSpace(tablespace) ? null : tablespace.Trim();
        return builder;
    }

    public static IndexBuilder<TEntity, TKey> HasStorageParameter<TEntity, TKey>(
        this IndexBuilder<TEntity, TKey> builder,
        PostgresIndexStorageParameter parameter,
        object value)
        where TEntity : class =>
        builder.HasStorageParameter(parameter.ToSqlName(), value);

    public static IndexBuilder<TEntity, TKey> HasStorageParameter<TEntity, TKey>(
        this IndexBuilder<TEntity, TKey> builder,
        string name,
        object value)
        where TEntity : class
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
        return builder;
    }
}
