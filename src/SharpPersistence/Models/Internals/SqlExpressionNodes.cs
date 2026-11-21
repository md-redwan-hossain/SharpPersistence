namespace SharpPersistence.Models.Internals;

internal sealed record SqlLiteralExpression(object? Value) : SqlExpression;
internal sealed record SqlColumnExpression(string Name, bool? Delimit = null) : SqlExpression;
internal sealed record SqlLengthExpression(SqlColumnExpression Column) : SqlExpression;
internal sealed record SqlBinaryExpression(SqlExpression Left, string Operator, SqlExpression Right) : SqlExpression;
internal sealed record SqlLogicalExpression(string Operator, IReadOnlyList<SqlExpression> Operands) : SqlExpression;
internal sealed record SqlNotExpression(SqlExpression Inner) : SqlExpression;
internal sealed record SqlInExpression(SqlExpression Target, IReadOnlyList<SqlExpression> Values, bool Negated) : SqlExpression;
internal sealed record SqlBetweenExpression(SqlExpression Target, SqlExpression Lower, SqlExpression Upper, bool Negated) : SqlExpression;
internal sealed record SqlNullCheckExpression(SqlExpression Target, bool Negated) : SqlExpression;
internal sealed record SqlCaseExpression(IReadOnlyList<(SqlExpression Condition, SqlExpression Then)> Branches, SqlExpression? Else) : SqlExpression;
internal sealed record SqlRawExpression(string Sql) : SqlExpression;
