using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

/// <summary>
/// Base SQL expression node supporting implicit conversions from CLR literal values.
/// </summary>
public abstract record SqlExpression
{
    internal SqlExpression() { }

    public static implicit operator SqlExpression(short value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(int value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(long value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(float value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(double value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(decimal value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(bool value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(string value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(Enum value) => new SqlLiteralExpression(value);
    public static implicit operator SqlExpression(Guid value) => new SqlLiteralExpression(value);
}
