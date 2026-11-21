using System.ComponentModel;
using System.Numerics;
using System.Text;
using Humanizer;
using SharpPersistence.Enums;
using SharpPersistence.Models.Internals;

namespace SharpPersistence.Generators;

/// <summary>
/// <c>SqlCheckConstraintGenerator</c> is a utility class for generating raw sql code for check-constraints in a type-safe manner.
/// It can be useful with Entity Framework Core since it doesn't have strongly-typed check-constraint support and requires raw SQL code.
/// </summary>
[Obsolete("Use SharpPersistence.SqlCheckConstraintBuilder instead.")]
public class SqlCheckConstraintGenerator
{
    private readonly bool _delimitString;
    private readonly SqlNamingConvention _sqlNamingConvention;
    private readonly Rdbms _rdbms;

    /// <param name="rdbms">determines the delimitString symbols based on database.</param>
    /// <param name="sqlNamingConvention">denotes the case of the generated SQL</param>
    public SqlCheckConstraintGenerator(Rdbms rdbms,
        SqlNamingConvention sqlNamingConvention)
    {
        _rdbms = rdbms;
        _sqlNamingConvention = sqlNamingConvention;
        _delimitString = true;
    }

    /// <param name="rdbms">determines the delimitString symbols based on database.</param>
    /// <param name="sqlNamingConvention">denotes the case of the generated SQL.</param>
    /// <param name="delimitString">any method parameter of this class which is related to string delimitation will override class level <c>delimitString</c> set by constructor. </param>
    public SqlCheckConstraintGenerator(Rdbms rdbms,
        SqlNamingConvention sqlNamingConvention,
        bool delimitString)
    {
        _rdbms = rdbms;
        _sqlNamingConvention = sqlNamingConvention;
        _delimitString = delimitString;
    }

    /// <summary>
    /// Starts building a SQL <c>CASE</c> expression for use in a check constraint.
    /// </summary>
    public IInitializedSqlCase Case() => new SqlCaseBuilder();

    public string And(string firstOperand, string secondOperand, params string[] otherOperands)
    {
        var sb = new StringBuilder($"{firstOperand} {SqlOperators.And} {secondOperand}");

        var queue = new Queue<string>(otherOperands);

        while (queue.TryDequeue(out var value))
        {
            sb.Append($" {SqlOperators.And} ");
            sb.Append(value);
        }

        return WrapWithParentheses(NormalizeAndTrim(sb));
    }

    public string Or(string firstOperand, string secondOperand, params string[] otherOperands)
    {
        var sb = new StringBuilder($"{firstOperand} {SqlOperators.Or} {secondOperand}");

        var queue = new Queue<string>(otherOperands);

        while (queue.TryDequeue(out var value))
        {
            sb.Append($" {SqlOperators.Or} ");
            sb.Append(value);
        }

        return WrapWithParentheses(NormalizeAndTrim(sb));
    }

    private (string left, string right) TransformCase(string left, string right)
    {
        switch (_sqlNamingConvention)
        {
            case SqlNamingConvention.LowerSnakeCase:
                return (left.Underscore().ToLowerInvariant(), right.Underscore().ToLowerInvariant());
            case SqlNamingConvention.UpperSnakeCase:
                return (left.Underscore().ToUpperInvariant(), right.Underscore().ToUpperInvariant());
            case SqlNamingConvention.PascalCase:
                return (left.Pascalize(), right.Pascalize());
            default:
                return (left, right);
        }
    }

    private static string GetComparisonOperatorString(SqlComparisonOperator comparisonOperator)
    {
        return comparisonOperator switch
        {
            SqlComparisonOperator.Equal => SqlOperators.Equal,
            SqlComparisonOperator.GreaterThan => SqlOperators.GreaterThan,
            SqlComparisonOperator.LessThan => SqlOperators.LessThan,
            SqlComparisonOperator.GreaterThanOrEqual => SqlOperators.GreaterThanOrEqual,
            SqlComparisonOperator.LessThanOrEqual => SqlOperators.LessThanOrEqual,
            SqlComparisonOperator.NotEqual => SqlOperators.NotEqual,
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonOperator))
        };
    }

    private string TransformCase(string columnOrOperand)
    {
        switch (_sqlNamingConvention)
        {
            case SqlNamingConvention.LowerSnakeCase:
                return columnOrOperand.Underscore().ToLowerInvariant();
            case SqlNamingConvention.UpperSnakeCase:
                return columnOrOperand.Underscore().ToUpperInvariant();
            case SqlNamingConvention.PascalCase:
                return columnOrOperand.Pascalize();
            default:
                return columnOrOperand;
        }
    }

    public string In(string leftOperand, ICollection<int> rightOperands, bool? delimitLeftOperand = null)
    {
        ThrowIfEmptyCollection(rightOperands, nameof(rightOperands));
        var transformed = TransformCase(leftOperand);

        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {SqlOperators.In} {WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))}";
    }

    public string In(string leftOperand, ICollection<string> rightOperands, bool? delimitLeftOperand = null)
    {
        ThrowIfEmptyCollection(rightOperands, nameof(rightOperands));
        var transformed = TransformCase(leftOperand);
        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {SqlOperators.In} {WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))}";
    }

    public string In(string leftOperand, ICollection<Enum> rightOperands, bool? delimitLeftOperand = null)
    {
        ThrowIfEmptyCollection(rightOperands, nameof(rightOperands));
        var transformed = TransformCase(leftOperand);
        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {SqlOperators.In} {WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))}";
    }

    public string NotIn(string leftOperand, ICollection<int> rightOperands, bool? delimitLeftOperand = null)
    {
        ThrowIfEmptyCollection(rightOperands, nameof(rightOperands));
        var transformed = TransformCase(leftOperand);
        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {SqlOperators.NotIn} {WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))}";
    }

    public string NotIn(string leftOperand, ICollection<string> rightOperands, bool? delimitLeftOperand = null)
    {
        ThrowIfEmptyCollection(rightOperands, nameof(rightOperands));
        var transformed = TransformCase(leftOperand);
        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {SqlOperators.NotIn} {WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))}";
    }

    public string NotIn(string leftOperand, ICollection<Enum> rightOperands, bool? delimitLeftOperand = null)
    {
        ThrowIfEmptyCollection(rightOperands, nameof(rightOperands));
        var transformed = TransformCase(leftOperand);
        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {SqlOperators.NotIn} {WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))}";
    }

    public string NotEqualTo(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);

        var right = rightOperandType == SqlOperandType.Column
            ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
            : SqlString(transformed.right);

        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.NotEqual} {right}";
    }

    public string NotEqualTo(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));

        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.NotEqual} {transformed.right}";
    }

    public string NotEqualTo(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);

        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        if (leftOperandSqlDataType is SqlDataType.VarChar or SqlDataType.Text)
        {
            leftOperandWithLogic = LengthOperatorHandler(leftOperandWithLogic);
        }

        return $"{leftOperandWithLogic} {SqlOperators.NotEqual} {rightOperand}";
    }

    public string NotEqualTo(string leftOperand, bool rightOperand, bool useIsNotOperator = false,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var op = useIsNotOperator ? SqlOperators.IsNot : SqlOperators.NotEqual;
        var val = rightOperand ? bool.TrueString.ToUpperInvariant() : bool.FalseString.ToUpperInvariant();
        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {op} {val}";
    }

    public string EqualTo(string leftOperand, bool rightOperand, bool useIsOperator = false,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var op = useIsOperator ? SqlOperators.Is : SqlOperators.Equal;
        var val = rightOperand ? bool.TrueString.ToUpperInvariant() : bool.FalseString.ToUpperInvariant();
        return $"{OperandHandler(transformed, delimitLeftOperand ?? _delimitString)} {op} {val}";
    }

    public string EqualTo(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        var right = rightOperandType == SqlOperandType.Column
            ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
            : SqlString(transformed.right);
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.Equal} {right}";
    }

    public string EqualTo(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        if (leftOperandSqlDataType is SqlDataType.VarChar or SqlDataType.Text)
        {
            leftOperandWithLogic = LengthOperatorHandler(leftOperandWithLogic);
        }

        return $"{leftOperandWithLogic} {SqlOperators.Equal} {rightOperand}";
    }

    public string EqualTo(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));

        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.Equal} {transformed.right}";
    }

    public string GreaterThan(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        var right = rightOperandType == SqlOperandType.Column
            ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
            : SqlString(transformed.right);

        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.GreaterThan} {right}";
    }

    public string GreaterThan(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.GreaterThan} {transformed.right}";
    }

    public string GreaterThan(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        if (leftOperandSqlDataType is SqlDataType.VarChar or SqlDataType.Text)
        {
            leftOperandWithLogic = LengthOperatorHandler(leftOperandWithLogic);
        }

        return $"{leftOperandWithLogic} {SqlOperators.GreaterThan} {rightOperand}";
    }

    public string GreaterThanOrEqual(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        var right = rightOperandType == SqlOperandType.Column
            ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
            : SqlString(transformed.right);
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.GreaterThanOrEqual} {right}";
    }

    public string GreaterThanOrEqual(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.GreaterThanOrEqual} {transformed.right}";
    }

    public string GreaterThanOrEqual(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        if (leftOperandSqlDataType is SqlDataType.VarChar or SqlDataType.Text)
        {
            leftOperandWithLogic = LengthOperatorHandler(leftOperandWithLogic);
        }

        return $"{leftOperandWithLogic} {SqlOperators.GreaterThanOrEqual} {rightOperand}";
    }

    public string LessThan(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        var right = rightOperandType == SqlOperandType.Column
            ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
            : SqlString(transformed.right);
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.LessThan} {right}";
    }

    public string LessThan(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.LessThan} {transformed.right}";
    }

    public string LessThan(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        if (leftOperandSqlDataType is SqlDataType.VarChar or SqlDataType.Text)
        {
            leftOperandWithLogic = LengthOperatorHandler(leftOperandWithLogic);
        }

        return $"{leftOperandWithLogic} {SqlOperators.LessThan} {rightOperand}";
    }

    public string LessThanOrEqual(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        var right = rightOperandType == SqlOperandType.Column
            ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
            : SqlString(transformed.right);
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.LessThanOrEqual} {right}";
    }

    public string LessThanOrEqual(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return $"{OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString)} {SqlOperators.LessThanOrEqual} {transformed.right}";
    }

    public string LessThanOrEqual(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        if (leftOperandSqlDataType is SqlDataType.VarChar or SqlDataType.Text)
        {
            leftOperandWithLogic = LengthOperatorHandler(leftOperandWithLogic);
        }

        return $"{leftOperandWithLogic} {SqlOperators.LessThanOrEqual} {rightOperand}";
    }

    public string Between(string columnName, string leftOperand,
        string rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return $"{OperandHandler(transformed, delimitColumnName ?? _delimitString)} {SqlOperators.Between} {SqlString(leftOperand)} {SqlOperators.And} {SqlString(rightOperand)}";
    }

    public string Between(string columnName, int leftOperand,
        int rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return $"{OperandHandler(transformed, delimitColumnName ?? _delimitString)} {SqlOperators.Between} {leftOperand} {SqlOperators.And} {rightOperand}";
    }

    public string Between(string columnName, double leftOperand,
        double rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return $"{OperandHandler(transformed, delimitColumnName ?? _delimitString)} {SqlOperators.Between} {leftOperand} {SqlOperators.And} {rightOperand}";
    }

    public string NotBetween(string columnName, string leftOperand,
        string rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return $"{OperandHandler(transformed, delimitColumnName ?? _delimitString)} {SqlOperators.NotBetween} {SqlString(leftOperand)} {SqlOperators.And} {SqlString(rightOperand)}";
    }

    public string NotBetween(string columnName, int leftOperand,
        int rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return $"{OperandHandler(transformed, delimitColumnName ?? _delimitString)} {SqlOperators.NotBetween} {leftOperand} {SqlOperators.And} {rightOperand}";
    }

    public string NotBetween(string columnName, double leftOperand,
        double rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return $"{OperandHandler(transformed, delimitColumnName ?? _delimitString)} {SqlOperators.NotBetween} {leftOperand} {SqlOperators.And} {rightOperand}";
    }

    public string IsNull(string leftOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        return NormalizeAndTrim($"{leftOperandWithLogic} {SqlOperators.Is} {SqlOperators.Null}");
    }

    public string IsNotNull(string leftOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        return NormalizeAndTrim($"{leftOperandWithLogic} {SqlOperators.IsNot} {SqlOperators.Null}");
    }

    public string Math<T>(ICollection<(string column, SqlMathOperator? mathOperator)> columnWithOperators,
        SqlComparisonOperator comparisonOperator, T value, bool? delimitColumns = null)
        where T : struct, INumber<T>
    {
        var transformed = columnWithOperators.Select(x => x with
        {
            column = OperandHandler(TransformCase(x.column), delimitColumns ?? _delimitString)
        });

        var sb = new StringBuilder();

        var queue = new Queue<(string column, SqlMathOperator? mathOperator)>(transformed);

        while (queue.TryDequeue(out var item))
        {
            sb.Append(item.column);

            switch (item.mathOperator)
            {
                case null:
                    continue;
                case SqlMathOperator.Add:
                    sb.Append($" {SqlOperators.Add} ");
                    break;
                case SqlMathOperator.Subtract:
                    sb.Append($" {SqlOperators.Subtract} ");
                    break;
                case SqlMathOperator.Multiply:
                    sb.Append($" {SqlOperators.Multiply} ");
                    break;
                case SqlMathOperator.Divide:
                    sb.Append($" {SqlOperators.Divide} ");
                    break;
                case SqlMathOperator.Modulo:
                    sb.Append($" {SqlOperators.Modulo} ");
                    break;
                default:
                    throw new InvalidEnumArgumentException();
            }
        }

        var str = sb.ToString();

        return NormalizeAndTrim($"{str} {GetComparisonOperatorString(comparisonOperator)} {value}");
    }

    private string LengthOperatorHandler(string data)
    {
        return _rdbms switch
        {
            Rdbms.SqlServer => $"LEN({data})",
            Rdbms.PostgreSql => $"length({data})",
            Rdbms.MySql => $"CHAR_LENGTH({data})",
            Rdbms.Oracle => $"LENGTH({data})",
            _ => data
        };
    }

    private string OperandHandler(string value, bool delimit) =>
        delimit ? DelimitString(value) : value;

    private static string EnumValueToString(IFormattable value) =>
        Convert.ToInt32(value).ToString();

    private static string CommaSeparatedCollectionData(ICollection<Enum> collection) =>
        CommaSeparatedCollectionDataMaker(collection, Convert.ToInt32);

    private static string CommaSeparatedCollectionData(ICollection<int> collection) =>
        CommaSeparatedCollectionDataMaker<int, int>(collection);

    private static string CommaSeparatedCollectionData(ICollection<string> collection) =>
        CommaSeparatedCollectionDataMaker(collection, SqlString);

    private static string CommaSeparatedCollectionDataMaker<TCollection, TValue>(
        ICollection<TCollection> collection, Func<TCollection, TValue>? logic = null)
    {
        var sb = new StringBuilder();
        var size = collection.Count;
        var counter = 0;

        foreach (var item in collection)
        {
            if (counter >= 1 && counter != size)
            {
                sb.Append(", ");
            }

            if (logic is not null)
            {
                sb.Append(logic(item));
            }
            else
            {
                sb.Append(item);
            }

            counter += 1;
        }

        return sb.ToString();
    }

    private static void ThrowIfEmptyCollection<T>(ICollection<T> collection, string paramName)
    {
        if (collection.Count == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", paramName);
        }
    }

    private static string WrapWithParentheses(string text) => $"({text})";

    private static string SqlString(string text) => $"'{text.Replace("'", "''")}'";

    private string DelimitString(string text)
    {
        return _rdbms switch
        {
            Rdbms.PostgreSql => $"\"{text}\"",
            Rdbms.MySql => $"`{text}`",
            Rdbms.SqlServer => $"[{text}]",
            Rdbms.Oracle => $"\"{text}\"",
            _ => text
        };
    }

    private static string NormalizeAndTrim(StringBuilder input)
    {
        if (input.Length == 0)
        {
            return input.ToString();
        }

        var writeIndex = 0;
        var skipped = false;

        for (var readIndex = 0; readIndex < input.Length; readIndex++)
        {
            var c = input[readIndex];
            if (char.IsWhiteSpace(c))
            {
                if (skipped) continue;
                input[writeIndex++] = ' ';
                skipped = true;
            }
            else
            {
                skipped = false;
                input[writeIndex++] = c;
            }
        }

        input.Length = writeIndex;
        return input.ToString().Trim();
    }

    private static string NormalizeAndTrim(string input)
    {
        return NormalizeAndTrim(new StringBuilder(input));
    }
}
