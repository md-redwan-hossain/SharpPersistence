using System.Text;
using Humanizer;
using SharpPersistence.Enums;

namespace SharpPersistence.Generators;

/// <summary>
/// <c>SqlCheckConstrainGenerator</c> is a utility class for generating raw sql code for check-constrains in a type-safe manner.
/// It can be useful with Entity Framework Core since it doesn't have strongly-typed check-constrain support and requires raw sql code.
/// </summary>
public class SqlCheckConstrainGenerator
{
    private readonly bool _delimitStringGlobalLevel;
    private readonly SqlNamingConvention _sqlNamingConvention;
    private readonly Rdbms _rdbms;

    /// <param name="rdbms">determines the delimitStringGlobalLevel symbols based on database.</param>
    /// <param name="sqlNamingConvention">denotes the case of the generated SQL</param>
    /// <param name="delimitStringGlobalLevel">any method parameter of this class which is related to string delimitation will override <c>delimitStringGlobal</c> </param>
    public SqlCheckConstrainGenerator(Rdbms rdbms,
        SqlNamingConvention sqlNamingConvention,
        bool delimitStringGlobalLevel = true)
    {
        _rdbms = rdbms;
        _sqlNamingConvention = sqlNamingConvention;
        _delimitStringGlobalLevel = delimitStringGlobalLevel;
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

    public string And(string firstOperand, string secondOperand, params string[] otherOperands)
    {
        var sb = new StringBuilder(string.Concat(firstOperand, AndSign, secondOperand));
        var size = otherOperands.Length;
        var counter = 0;

        if (size > 0)
        {
            sb.Append(AndSign);
        }

        foreach (var operand in otherOperands)
        {
            sb.Append(operand);

            if (counter >= 1 && counter != size)
            {
                sb.Append(AndSign);
            }

            counter += 1;
        }

        return WrapWithParentheses(NormalizeAndTrimWhiteSpace(sb));
    }

    public string Or(string firstOperand, string secondOperand, params string[] otherOperands)
    {
        var sb = new StringBuilder(string.Concat(firstOperand, OrSign, secondOperand));
        var size = otherOperands.Length;
        var counter = 0;

        if (size > 0)
        {
            sb.Append(OrSign);
        }

        foreach (var operand in otherOperands)
        {
            sb.Append(operand);

            if (counter >= 1 && counter != size)
            {
                sb.Append(OrSign);
            }

            counter += 1;
        }

        return WrapWithParentheses(NormalizeAndTrimWhiteSpace(sb));
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
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            InSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string In(string leftOperand, ICollection<string> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            InSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string In(string leftOperand, ICollection<Enum> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            InSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotIn(string leftOperand, ICollection<int> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            NotInSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotIn(string leftOperand, ICollection<string> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            NotInSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotIn(string leftOperand, ICollection<Enum> rightOperands, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            NotInSign,
            WrapWithParentheses(CommaSeparatedCollectionData(rightOperands))
        );
    }

    public string NotEqualTo(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);

        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            NotEqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitStringGlobalLevel)
                : SqlString(transformed.right)
        );
    }

    public string NotEqualTo(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));

        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            NotEqualSign,
            transformed.right
        );
    }

    public string NotEqualTo(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);

        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
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

    public string NotEqualTo(string leftOperand, bool rightOperand,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            NotEqualSign,
            rightOperand ? bool.TrueString : bool.FalseString
        );
    }

    public string EqualTo(string leftOperand, bool rightOperand,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        return string.Concat(
            OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel),
            EqualSign,
            rightOperand ? bool.TrueString : bool.FalseString
        );
    }

    public string EqualTo(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            EqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitStringGlobalLevel)
                : SqlString(transformed.right)
        );
    }

    public string EqualTo(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
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
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            EqualSign,
            transformed.right
        );
    }

    public string GreaterThan(string leftOperand, string rightOperand, SqlOperandType rightOperandType,
        bool? delimitLeftOperand = null, bool? delimitRightOperand = null)
    {
        var transformed = TransformCase(leftOperand, rightOperand);

        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            GreaterThanSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitStringGlobalLevel)
                : SqlString(transformed.right)
        );
    }

    public string GreaterThan(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            GreaterThanSign,
            transformed.right
        );
    }

    public string GreaterThan(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
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
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            GreaterThanOrEqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitStringGlobalLevel)
                : SqlString(transformed.right)
        );
    }

    public string GreaterThanOrEqual(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            GreaterThanOrEqualSign,
            transformed.right
        );
    }

    public string GreaterThanOrEqual(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
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
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            LessThanSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitStringGlobalLevel)
                : SqlString(transformed.right)
        );
    }

    public string LessThan(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            LessThanSign,
            transformed.right
        );
    }

    public string LessThan(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
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
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            LessThanOrEqualSign,
            rightOperandType == SqlOperandType.Column
                ? OperandHandler(transformed.right, delimitRightOperand ?? _delimitStringGlobalLevel)
                : SqlString(transformed.right)
        );
    }

    public string LessThanOrEqual(string leftOperand, Enum rightOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand, EnumValueToString(rightOperand));
        return string.Concat(
            OperandHandler(transformed.left, delimitLeftOperand ?? _delimitStringGlobalLevel),
            LessThanOrEqualSign,
            transformed.right
        );
    }

    public string LessThanOrEqual(string leftOperand, int rightOperand, SqlDataType leftOperandSqlDataType,
        bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
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
            OperandHandler(transformed, delimitColumnName ?? _delimitStringGlobalLevel),
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
            OperandHandler(transformed, delimitColumnName ?? _delimitStringGlobalLevel),
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
            OperandHandler(transformed, delimitColumnName ?? _delimitStringGlobalLevel),
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
            OperandHandler(transformed, delimitColumnName ?? _delimitStringGlobalLevel),
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
            OperandHandler(transformed, delimitColumnName ?? _delimitStringGlobalLevel),
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
            OperandHandler(transformed, delimitColumnName ?? _delimitStringGlobalLevel),
            NotBetweenSign,
            leftOperand,
            AndSign,
            rightOperand
        );
    }

    public string IsNull(string leftOperand, bool? delimitLeftOperand = null)
    {
        var transformed = TransformCase(leftOperand);
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
        return NormalizeAndTrimWhiteSpace(
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
        var leftOperandWithLogic = OperandHandler(transformed, delimitLeftOperand ?? _delimitStringGlobalLevel);
        return NormalizeAndTrimWhiteSpace(
            string.Concat(
                leftOperandWithLogic,
                IsNot,
                Null
            )
        );
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
        ICollection<TCollection> collection,
        Func<TCollection, TValue>? logic = null)
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

    private static string NormalizeAndTrimWhiteSpace(StringBuilder input)
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

    private static string NormalizeAndTrimWhiteSpace(string input)
    {
        return NormalizeAndTrimWhiteSpace(new StringBuilder(input));
    }
}