using System.Numerics;
using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

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

    public static SqlNumericExpression<TEntity> Length<TEntity>(
        this SqlColumnBuilder<TEntity, string> column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlNumericExpression<TEntity>(new SqlLengthExpression(column.Node), column.Context);
    }

    public static SqlPredicate IsTrue<TEntity>(
        this SqlColumnBuilder<TEntity, bool> column,
        bool useIsOperator = false)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlPredicate(
            new SqlBinaryExpression(column.Node, useIsOperator ? SqlOperators.Is : SqlOperators.Equal, new SqlLiteralExpression(true)),
            column.Context);
    }

    public static SqlPredicate IsFalse<TEntity>(
        this SqlColumnBuilder<TEntity, bool> column,
        bool useIsOperator = false)
    {
        ArgumentNullException.ThrowIfNull(column);
        return new SqlPredicate(
            new SqlBinaryExpression(column.Node, useIsOperator ? SqlOperators.Is : SqlOperators.Equal, new SqlLiteralExpression(false)),
            column.Context);
    }
}
