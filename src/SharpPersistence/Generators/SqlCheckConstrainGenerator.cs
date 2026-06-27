using System.ComponentModel;
using System.Numerics;
using System.Text;
using Humanizer;
using SharpPersistence.Enums;

namespace SharpPersistence.Generators;

/// <summary>
/// <c>SqlCheckConstrainGenerator</c> is a utility class for generating raw sql code for check-constrains in a type-safe manner.
/// It can be useful with Entity Framework Core since it doesn't have strongly-typed check-constrain support and requires raw SQL code.
/// </summary>
public class SqlCheckConstrainGenerator
{
    private readonly bool _delimitString;
    private readonly SqlNamingConvention _sqlNamingConvention;
    private readonly Rdbms _rdbms;

    /// <param name="rdbms">determines the delimitString symbols based on database.</param>
    /// <param name="sqlNamingConvention">denotes the case of the generated SQL</param>
    public SqlCheckConstrainGenerator(Rdbms rdbms,
        SqlNamingConvention sqlNamingConvention)
    {
        _rdbms = rdbms;
        _sqlNamingConvention = sqlNamingConvention;
        _delimitString = true;
    }

    /// <param name="rdbms">determines the delimitString symbols based on database.</param>
    /// <param name="sqlNamingConvention">denotes the case of the generated SQL.</param>
    /// <param name="delimitString">any method parameter of this class which is related to string delimitation will override class level <c>delimitString</c> set by constructor. </param>
    public SqlCheckConstrainGenerator(Rdbms rdbms,
        SqlNamingConvention sqlNamingConvention,
        bool delimitString)
    {
        _rdbms = rdbms;
        _sqlNamingConvention = sqlNamingConvention;
        _delimitString = delimitString;
    }

    private const string EqualSign = " = ";
    private const string GreaterThanSign = " > ";
    private const string LessThanSign = " < ";
    private const string GreaterThanOrEqualSign = " >= ";
    private const string LessThanOrEqualSign = " <= ";
    private const string NotEqualSign = " <> ";
    private const string OrSign = " OR ";
    private const string AndSign = " AND ";
    private const string InSign = " IN ";
    private const string Is = " IS ";
    private const string IsNot = " IS NOT ";
    private const string Null = " NULL ";
    private const string NotInSign = " NOT IN ";
    private const string BetweenSign = " BETWEEN ";
    private const string NotBetweenSign = " NOT BETWEEN ";
    private const string MathAdd = " + ";
    private const string MathSubtract = " - ";
    private const string MathMultiply = " * ";
    private const string MathDivide = " / ";
    private const string MathModulo = " % ";

    public string And(string firstOperand, string secondOperand, params string[] otherOperands)
    {
        var sb = new StringBuilder(string.Concat(firstOperand, AndSign, secondOperand));

        var queue = new Queue<string>(otherOperands);

        while (queue.TryDequeue(out var value))
        {
            sb.Append(AndSign);
            sb.Append(value);
        }

        return WrapWithParentheses(NormalizeAndTrim(sb));
    }

    public string Or(string firstOperand, string secondOperand, params string[] otherOperands)
    {
        var sb = new StringBuilder(string.Concat(firstOperand, OrSign, secondOperand));

        var queue = new Queue<string>(otherOperands);

        while (queue.TryDequeue(out var value))
        {
            sb.Append(OrSign);
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

    private static string GetComparisionOperatorString(SqlComparisionOperator comparisionOperator)
    {
        return comparisionOperator switch
        {
            SqlComparisionOperator.Equal => EqualSign,
            SqlComparisionOperator.GreaterThan => GreaterThanSign,
            SqlComparisionOperator.LessThan => LessThanSign,
            SqlComparisionOperator.GreaterThanOrEqual => GreaterThanOrEqualSign,
            SqlComparisionOperator.LessThanOrEqual => LessThanOrEqualSign,
            SqlComparisionOperator.NotEqual => NotEqualSign,
            _ => throw new ArgumentOutOfRangeException(nameof(comparisionOperator))
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
        var transformed = TransformCase(leftOperand);

        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            InSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string In(string leftOperand, ICollection<string> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            InSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string In(string leftOperand, ICollection<Enum> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            InSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotIn(string leftOperand, ICollection<int> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            NotInSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotIn(string leftOperand, ICollection<string> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            NotInSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotIn(string leftOperand, ICollection<Enum> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            NotInSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotEqualTo(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);

        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            NotEqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
                : SqlString(transformed.right)
        );
    }

    public string NotEqualTo(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));

        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            NotEqualSign,
            transformed.right
        );
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

        return string.Concat(
            leftOperandWithLogic,
            NotEqualSign,
            rightOperand
        );
    }

    public string NotEqualTo(string leftOperand, bool rightOperand, bool useIsNotOperator = false,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            useIsNotOperator ? IsNot : NotEqualSign,
            rightOperand ? bool.TrueString.ToUpperInvariant() : bool.FalseString.ToUpperInvariant()
        );
    }

    public string EqualTo(string leftOperand, bool rightOperand, bool useIsOperator = false,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitString),
            useIsOperator ? Is : EqualSign,
            rightOperand ? bool.TrueString.ToUpperInvariant() : bool.FalseString.ToUpperInvariant()
        );
    }

    public string EqualTo(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            EqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
                : SqlString(transformed.right)
        );
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

        return string.Concat(
            leftOperandWithLogic,
            EqualSign,
            rightOperand
        );
    }

    public string EqualTo(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));

        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            EqualSign,
            transformed.right
        );
    }

    public string GreaterThan(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);

        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            GreaterThanSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
                : SqlString(transformed.right)
        );
    }

    public string GreaterThan(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            GreaterThanSign,
            transformed.right
        );
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

        return string.Concat(
            leftOperandWithLogic,
            GreaterThanSign,
            rightOperand
        );
    }

    public string GreaterThanOrEqual(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            GreaterThanOrEqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
                : SqlString(transformed.right)
        );
    }

    public string GreaterThanOrEqual(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            GreaterThanOrEqualSign,
            transformed.right
        );
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

        return string.Concat(
            leftOperandWithLogic,
            GreaterThanOrEqualSign,
            rightOperand
        );
    }

    public string LessThan(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            LessThanSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
                : SqlString(transformed.right)
        );
    }

    public string LessThan(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            LessThanSign,
            transformed.right
        );
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

        return string.Concat(
            leftOperandWithLogic,
            LessThanSign,
            rightOperand
        );
    }

    public string LessThanOrEqual(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            LessThanOrEqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitString)
                : SqlString(transformed.right)
        );
    }

    public string LessThanOrEqual(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitString),
            LessThanOrEqualSign,
            transformed.right
        );
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

        return string.Concat(
            leftOperandWithLogic,
            LessThanOrEqualSign,
            rightOperand
        );
    }

    public string Between(string columnName, string leftOperand,
        string rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return string.Concat(
            OperandHandler(transformed, delimitColumnName ?? _delimitString),
            BetweenSign,
            SqlString(leftOperand),
            AndSign,
            SqlString(rightOperand)
        );
    }

    public string Between(string columnName, int leftOperand,
        int rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return string.Concat(
            OperandHandler(transformed, delimitColumnName ?? _delimitString),
            BetweenSign,
            leftOperand,
            AndSign,
            rightOperand
        );
    }

    public string Between(string columnName, double leftOperand,
        double rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return string.Concat(
            OperandHandler(transformed, delimitColumnName ?? _delimitString),
            BetweenSign,
            leftOperand,
            AndSign,
            rightOperand
        );
    }

    public string NotBetween(string columnName, string leftOperand,
        string rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return string.Concat(
            OperandHandler(transformed, delimitColumnName ?? _delimitString),
            NotBetweenSign,
            SqlString(leftOperand),
            AndSign,
            SqlString(rightOperand)
        );
    }

    public string NotBetween(string columnName, int leftOperand,
        int rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return string.Concat(
            OperandHandler(transformed, delimitColumnName ?? _delimitString),
            NotBetweenSign,
            leftOperand,
            AndSign,
            rightOperand
        );
    }

    public string NotBetween(string columnName, double leftOperand,
        double rightOperand, bool? delimitColumnName = null)
    {
        var transformed = TransformCase(columnName);
        return string.Concat(
            OperandHandler(transformed, delimitColumnName ?? _delimitString),
            NotBetweenSign,
            leftOperand,
            AndSign,
            rightOperand
        );
    }

    public string IsNull(string leftOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        return NormalizeAndTrim(
            string.Concat(
                leftOperandWithLogic,
                Is,
                Null
            )
        );
    }

    public string IsNotNull(string leftOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitString);
        return NormalizeAndTrim(
            string.Concat(
                leftOperandWithLogic,
                IsNot,
                Null
            )
        );
    }

    public string Math<T>(ICollection<(string column, SqlMathOperator? mathOperator)> columnWithOperators,
        SqlComparisionOperator comparisionOperator, T value, bool? delimitColumns = null)
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
                    sb.Append(MathAdd);
                    break;
                case SqlMathOperator.Subtract:
                    sb.Append(MathSubtract);
                    break;
                case SqlMathOperator.Multiply:
                    sb.Append(MathMultiply);
                    break;
                case SqlMathOperator.Divide:
                    sb.Append(MathDivide);
                    break;
                case SqlMathOperator.Modulo:
                    sb.Append(MathModulo);
                    break;
                default:
                    throw new InvalidEnumArgumentException();
            }
        }

        var str = sb.ToString();

        return NormalizeAndTrim(string.Concat(
            str,
            GetComparisionOperatorString(comparisionOperator),
            value
        ));
    }

    private string LengthOperatorHandler(string data)
    {
        return _rdbms switch
        {
            Rdbms.SqlServer => $"LEN({data})",
            Rdbms.PostgreSql => $"length({data})",
            Rdbms.MySql => $"CHAR_LENGTH({data})",
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

    private static string WrapWithParentheses(string text) => $"({text})";

    private static string SqlString(string text) => $"'{text.Replace("'", "''")}'";

    private string DelimitString(string text)
    {
        return _rdbms switch
        {
            Rdbms.PostgreSql => $"\"{text}\"",
            Rdbms.MySql => $"`{text}`",
            Rdbms.SqlServer => $"[{text}]",
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