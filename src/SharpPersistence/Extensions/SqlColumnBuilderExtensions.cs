using System.Numerics;
using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

/// <summary>
/// Type-constrained extension methods for <see cref="SqlColumnBuilder{TEntity, TProp}"/>.
/// </summary>
public static class SqlColumnBuilderExtensions
{
    public static SqlNumericExpression<TEntity> Math<TEntity, TProp>(
        this SqlColumnBuilder<TEntity, TProp> column)
        where TProp : INumber<TProp>
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlNumericExpression<TEntity>(column.Node, column.Context);
    }

    public static SqlNumericExpression<TEntity> Math<TEntity, TProp>(
        this SqlColumnBuilder<TEntity, TProp?> column)
        where TProp : struct, INumber<TProp>
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlNumericExpression<TEntity>(column.Node, column.Context);
    }

    public static SqlNumericComparison<TEntity> Length<TEntity>(
        this SqlColumnBuilder<TEntity, string> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlNumericComparison<TEntity>(new SqlLengthExpression(column.Node), column.Context);
    }

    public static SqlColumnBuilder<TEntity, string> Trim<TEntity>(
        this SqlColumnBuilder<TEntity, string> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, string>(new SqlTrimExpression(column.Node), column.Context);
    }

    public static SqlColumnBuilder<TEntity, string> Lower<TEntity>(
        this SqlColumnBuilder<TEntity, string> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, string>(new SqlCaseTransformExpression(column.Node, "LOWER"), column.Context);
    }

    public static SqlColumnBuilder<TEntity, string> Upper<TEntity>(
        this SqlColumnBuilder<TEntity, string> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, string>(new SqlCaseTransformExpression(column.Node, "UPPER"), column.Context);
    }

    public static SqlPredicate Like<TEntity>(
        this SqlColumnBuilder<TEntity, string> column,
        string pattern)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(pattern);
        return new SqlPredicate(new SqlBinaryExpression(column.Node, SqlOperators.Like, new SqlLiteralExpression(pattern)), column.Context);
    }

    public static SqlPredicate NotLike<TEntity>(
        this SqlColumnBuilder<TEntity, string> column,
        string pattern)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(pattern);
        return new SqlPredicate(new SqlBinaryExpression(column.Node, SqlOperators.NotLike, new SqlLiteralExpression(pattern)), column.Context);
    }

    public static SqlColumnBuilder<TEntity, TEnum> EnumAsString<TEntity, TEnum>(
        this SqlColumnBuilder<TEntity, TEnum> column)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, TEnum>(column.Node, column.Context, enumAsString: true);
    }

    public static SqlColumnBuilder<TEntity, TEnum?> EnumAsString<TEntity, TEnum>(
        this SqlColumnBuilder<TEntity, TEnum?> column)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, TEnum?>(column.Node, column.Context, enumAsString: true);
    }

    public static SqlColumnBuilder<TEntity, TEnum> EnumAsNumeric<TEntity, TEnum>(
        this SqlColumnBuilder<TEntity, TEnum> column)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, TEnum>(column.Node, column.Context, enumAsString: false);
    }

    public static SqlColumnBuilder<TEntity, TEnum?> EnumAsNumeric<TEntity, TEnum>(
        this SqlColumnBuilder<TEntity, TEnum?> column)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, TEnum?>(column.Node, column.Context, enumAsString: false);
    }

    public static SqlColumnBuilder<TEntity, int> BoolAsNumeric<TEntity>(
        this SqlColumnBuilder<TEntity, bool> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, int>(column.Node, column.Context);
    }

    public static SqlColumnBuilder<TEntity, int?> BoolAsNumeric<TEntity>(
        this SqlColumnBuilder<TEntity, bool?> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, int?>(column.Node, column.Context);
    }

    public static SqlColumnBuilder<TEntity, string> BoolAsString<TEntity>(
        this SqlColumnBuilder<TEntity, bool> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, string>(column.Node, column.Context);
    }

    public static SqlColumnBuilder<TEntity, string?> BoolAsString<TEntity>(
        this SqlColumnBuilder<TEntity, bool?> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlColumnBuilder<TEntity, string?>(column.Node, column.Context);
    }
}
