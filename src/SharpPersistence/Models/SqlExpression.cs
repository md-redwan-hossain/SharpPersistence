using SharpPersistence.Models.Internals;

namespace SharpPersistence;

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
