using System.Globalization;
using SharpPersistence.Migrator.Builders;

namespace SharpPersistence.Migrator.PostgreSql;

public static class PostgreSqlEntityBuilderExtensions
{
    public static EntityBuilder<TEntity> IsUnlogged<TEntity>(
        this EntityBuilder<TEntity> builder,
        bool unlogged = true)
        where TEntity : class
    {
        builder.Definition.IsUnlogged = unlogged;
        return builder;
    }

    public static EntityBuilder<TEntity> UseTablespace<TEntity>(
        this EntityBuilder<TEntity> builder,
        string? tablespace)
        where TEntity : class
    {
        builder.Definition.SetTablespace(tablespace);
        return builder;
    }

    public static PostgresExclusionConstraintBuilder<TEntity> HasExclusionConstraint<TEntity>(
        this EntityBuilder<TEntity> builder,
        string constraintName)
        where TEntity : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraintName);
        var def = builder.Definition.AddExclusionConstraint(constraintName.Trim());
        return new PostgresExclusionConstraintBuilder<TEntity>(def);
    }

    public static EntityBuilder<TEntity> HasStorageParameter<TEntity>(
        this EntityBuilder<TEntity> builder,
        PostgresStorageParameter parameter,
        object value)
        where TEntity : class =>
        builder.HasStorageParameter(parameter.ToSqlName(), value);

    public static EntityBuilder<TEntity> HasStorageParameter<TEntity>(
        this EntityBuilder<TEntity> builder,
        string name,
        object value)
        where TEntity : class
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Storage parameter name cannot be empty.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(value);

        var formatted = value switch
        {
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        builder.Definition.SetStorageParameter(name, formatted);
        return builder;
    }
}
