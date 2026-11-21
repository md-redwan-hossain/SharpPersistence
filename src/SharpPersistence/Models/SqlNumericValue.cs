using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

public readonly record struct SqlNumericValue
{
    internal SqlLiteralExpression Expression { get; }

    internal SqlNumericValue(SqlLiteralExpression expression) => Expression = expression;

    public static implicit operator SqlNumericValue(short value) => new(new SqlLiteralExpression(value));
    public static implicit operator SqlNumericValue(int value) => new(new SqlLiteralExpression(value));
    public static implicit operator SqlNumericValue(long value) => new(new SqlLiteralExpression(value));
    public static implicit operator SqlNumericValue(float value) => new(new SqlLiteralExpression(value));
    public static implicit operator SqlNumericValue(double value) => new(new SqlLiteralExpression(value));
    public static implicit operator SqlNumericValue(decimal value) => new(new SqlLiteralExpression(value));
}
