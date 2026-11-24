using System.Globalization;
using Humanizer;
using SharpPersistence.Enums;
using SharpPersistence.Models.Internals;

namespace SharpPersistence.Builders;

internal static class SqlCheckRenderer
{
    public static string Render(SqlExpression expression, SqlRenderContext ctx) => expression switch
    {
        SqlRawExpression raw => raw.Sql,
        SqlLiteralExpression lit => FormatLiteral(lit.Value, ctx.Dialect),
        SqlColumnExpression col => FormatColumn(col, ctx),
        SqlLengthExpression len => FormatLength(Render(len.Target, ctx), ctx.Dialect),
        SqlAbsExpression abs => $"ABS({Render(abs.Target, ctx)})",
        SqlTrimExpression trim => $"TRIM({Render(trim.Target, ctx)})",
        SqlCaseTransformExpression ct => $"{ct.FunctionName}({Render(ct.Target, ctx)})",
        SqlCoalesceExpression coal => $"COALESCE({Render(coal.Target, ctx)}, {Render(coal.Fallback, ctx)})",
        SqlNotExpression not => $"{SqlOperators.Not} ({Render(not.Inner, ctx)})",
        SqlNullCheckExpression nc => $"{Render(nc.Target, ctx)} {(nc.Negated ? $"{SqlOperators.IsNot} {SqlOperators.Null}" : $"{SqlOperators.Is} {SqlOperators.Null}")}",
        SqlBetweenExpression b => $"{Render(b.Target, ctx)} {(b.Negated ? SqlOperators.NotBetween : SqlOperators.Between)} {Render(b.Lower, ctx)} {SqlOperators.And} {Render(b.Upper, ctx)}",
        SqlInExpression i => $"{Render(i.Target, ctx)} {(i.Negated ? SqlOperators.NotIn : SqlOperators.In)} ({string.Join(", ", i.Values.Select(v => Render(v, ctx)))})",
        SqlLogicalExpression log => $"({string.Join($" {log.Operator} ", log.Operands.Select(o => Render(o, ctx)))})",
        SqlBinaryExpression bin => FormatBinary(bin, ctx),
        SqlCaseExpression c => FormatCase(c, ctx),
        _ => throw new NotSupportedException($"Unsupported expression node: {expression.GetType().Name}")
    };

    private static string FormatBinary(SqlBinaryExpression bin, SqlRenderContext ctx)
    {
        var left = ShouldWrapOperand(bin.Operator, bin.Left, isRightOperand: false)
            ? $"({Render(bin.Left, ctx)})"
            : Render(bin.Left, ctx);

        if (bin.Right is SqlLiteralExpression { Value: null })
        {
            if (bin.Operator == SqlOperators.Equal)
            {
                return $"{left} {SqlOperators.Is} {SqlOperators.Null}";
            }

            if (bin.Operator == SqlOperators.NotEqual)
            {
                return $"{left} {SqlOperators.IsNot} {SqlOperators.Null}";
            }
        }

        var right = ShouldWrapOperand(bin.Operator, bin.Right, isRightOperand: true)
            ? $"({Render(bin.Right, ctx)})"
            : Render(bin.Right, ctx);

        return $"{left} {bin.Operator} {right}";
    }

    private static bool ShouldWrapOperand(string parentOp, SqlExpression operand, bool isRightOperand)
    {
        if (operand is SqlCaseExpression)
        {
            return true;
        }

        if (operand is not SqlBinaryExpression child)
        {
            return false;
        }

        var parentPrec = GetArithmeticPrecedence(parentOp);
        var childPrec = GetArithmeticPrecedence(child.Operator);

        if (parentPrec == 0 || childPrec == 0)
        {
            return false;
        }

        if (childPrec < parentPrec)
        {
            return true;
        }

        return isRightOperand && childPrec == parentPrec && parentOp is SqlOperators.Subtract or SqlOperators.Divide or SqlOperators.Modulo;
    }

    private static int GetArithmeticPrecedence(string op) => op switch
    {
        SqlOperators.Add or SqlOperators.Subtract => 1,
        SqlOperators.Multiply or SqlOperators.Divide or SqlOperators.Modulo => 2,
        _ => 0
    };

    private static string FormatCase(SqlCaseExpression c, SqlRenderContext ctx)
    {
        var whens = string.Join(" ", c.Branches.Select(b => $"WHEN {Render(b.Condition, ctx)} THEN {Render(b.Then, ctx)}"));
        var elsePart = c.Else is not null ? $" ELSE {Render(c.Else, ctx)}" : string.Empty;
        return $"CASE {whens}{elsePart} END";
    }

    private static string FormatColumn(SqlColumnExpression col, SqlRenderContext ctx)
    {
        var cased = col.ApplyCase
            ? ctx.IdentifierCase switch
            {
                SqlIdentifierCase.LowerSnakeCase => col.Name.Underscore().ToLowerInvariant(),
                SqlIdentifierCase.UpperSnakeCase => col.Name.Underscore().ToUpperInvariant(),
                SqlIdentifierCase.PascalCase => col.Name.Pascalize(),
                SqlIdentifierCase.CamelCase => col.Name.Camelize(),
                SqlIdentifierCase.LowerCase => col.Name.ToLowerInvariant(),
                SqlIdentifierCase.UpperCase => col.Name.ToUpperInvariant(),
                _ => col.Name
            }
            : col.Name;

        var delimit = col.Delimit ?? ctx.DelimitIdentifiers;
        if (!delimit)
        {
            return cased;
        }

        return ctx.Dialect switch
        {
            SqlDialect.PostgreSql or SqlDialect.Oracle or SqlDialect.Sqlite => $"\"{cased}\"",
            SqlDialect.MySql => $"`{cased}`",
            SqlDialect.SqlServer => $"[{cased}]",
            _ => cased
        };
    }

    private static string FormatLength(string columnSql, SqlDialect dialect) => dialect switch
    {
        SqlDialect.SqlServer => $"LEN({columnSql})",
        SqlDialect.PostgreSql => $"length({columnSql})",
        SqlDialect.MySql => $"CHAR_LENGTH({columnSql})",
        SqlDialect.Oracle or SqlDialect.Sqlite => $"LENGTH({columnSql})",
        _ => columnSql
    };

    private static string FormatLiteral(object? value, SqlDialect dialect) => value switch
    {
        null => SqlOperators.Null,
        bool b => dialect == SqlDialect.SqlServer
            ? b ? "1" : "0"
            : b ? "TRUE" : "FALSE",
        Enum e => Convert.ToInt32(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        string s => $"'{s.Replace("'", "''")}'",
        Guid g => $"'{g}'",
        DateOnly d => $"'{d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'",
        TimeOnly t => $"'{t.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}'",
        DateTime dt => $"'{dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}'",
        DateTimeOffset dto => $"'{dto.ToString("yyyy-MM-dd HH:mm:sszzz", CultureInfo.InvariantCulture)}'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? SqlOperators.Null
    };
}
