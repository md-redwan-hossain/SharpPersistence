namespace SharpPersistence.Models.Internals;

internal static class SqlOperators
{
    public const string Equal = "=";
    public const string GreaterThan = ">";
    public const string LessThan = "<";
    public const string GreaterThanOrEqual = ">=";
    public const string LessThanOrEqual = "<=";
    public const string NotEqual = "<>";

    public const string Or = "OR";
    public const string And = "AND";
    public const string Not = "NOT";

    public const string In = "IN";
    public const string NotIn = "NOT IN";
    public const string Like = "LIKE";
    public const string NotLike = "NOT LIKE";
    public const string Is = "IS";
    public const string IsNot = "IS NOT";
    public const string Null = "NULL";
    public const string Between = "BETWEEN";
    public const string NotBetween = "NOT BETWEEN";

    public const string Add = "+";
    public const string Subtract = "-";
    public const string Multiply = "*";
    public const string Divide = "/";
    public const string Modulo = "%";
}
