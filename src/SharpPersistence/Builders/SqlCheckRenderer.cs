using System.Globalization;
using Humanizer;
using SharpPersistence.Enums;
using SharpPersistence.Models.Internals;

namespace SharpPersistence.Builders.Internal;

internal static class SqlCheckRenderer
{
    public static string Render(SqlExpression expression, SqlRenderContext ctx) => expression switch
    {
        SqlRawExpression raw => raw.Sql,
        SqlLiteralExpression lit => FormatLiteral(lit.Value),
        SqlColumnExpression col => FormatColumn(col, ctx),
        SqlLengthExpression len => FormatLength(FormatColumn(len.Column, ctx), ctx.Dialect),
        SqlNotExpression not => $"NOT ({Render(not.Inner, ctx)})",
        SqlNullCheckExpression nc => $"{Render(nc.Target, ctx)} {(nc.Negated ? "IS NOT NULL" : "IS NULL")}",
        SqlBetweenExpression b => $"{Render(b.Target, ctx)} {(b.Negated ? "NOT BETWEEN" : "BETWEEN")} {Render(b.Lower, ctx)} AND {Render(b.Upper, ctx)}",
        SqlInExpression i => $"{Render(i.Target, ctx)} {(i.Negated ? "NOT IN" : "IN")} ({string.Join(", ", i.Values.Select(v => Render(v, ctx)))})",
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

        return isRightOperand && childPrec == parentPrec && parentOp is "-" or "/" or "%";
    }

    private static int GetArithmeticPrecedence(string op) => op switch
    {
        "+" or "-" => 1,
        "*" or "/" or "%" => 2,
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
        var cased = ctx.IdentifierCase switch
        {
            SqlIdentifierCase.LowerSnakeCase => col.Name.Underscore().ToLowerInvariant(),
            SqlIdentifierCase.UpperSnakeCase => col.Name.Underscore().ToUpperInvariant(),
            SqlIdentifierCase.PascalCase => col.Name.Pascalize(),
            _ => col.Name
        };

        var delimit = col.Delimit ?? ctx.DelimitIdentifiers;
        if (!delimit)
        {
            return cased;
        }

        return ctx.Dialect switch
        {
            SqlDialect.PostgreSql or SqlDialect.Oracle => $"\"{cased}\"",
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
        SqlDialect.Oracle => $"LENGTH({columnSql})",
        _ => columnSql
    };

    private static string FormatLiteral(object? value) => value switch
    {
        null => "NULL",
        bool b => b ? "TRUE" : "FALSE",
        Enum e => Convert.ToInt32(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        string s => $"'{s.Replace("'", "''")}'",
        Guid g => $"'{g}'",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "NULL"
    };
}
