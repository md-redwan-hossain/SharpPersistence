using System.Linq.Expressions;
using System.Numerics;
using SharpPersistence.Builders;
using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

public sealed class SqlColumnBuilder<TEntity, TProp>
{
    internal SqlExpression Node { get; }
    internal SqlRenderContext Context { get; }

    internal SqlColumnBuilder(SqlExpression node, SqlRenderContext context)
    {
        Node = node;
        Context = context;
    }

    public SqlColumnBuilder<TEntity, TProp> WithDelimiter()
    {
        if (Node is not SqlColumnExpression col)
        {
            throw new InvalidOperationException("Delimiter modifiers can only be applied directly to a column.");
        }

        return new SqlColumnBuilder<TEntity, TProp>(col with { Delimit = true }, Context);
    }

    public SqlColumnBuilder<TEntity, TProp> WithoutDelimiter()
    {
        if (Node is not SqlColumnExpression col)
        {
            throw new InvalidOperationException("Delimiter modifiers can only be applied directly to a column.");
        }

        return new SqlColumnBuilder<TEntity, TProp>(col with { Delimit = false }, Context);
    }

    public SqlColumnBuilder<TEntity, TProp> Coalesce(TProp fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return new SqlColumnBuilder<TEntity, TProp>(new SqlCoalesceExpression(Node, new SqlLiteralExpression(fallback)),
            Context);
    }

    public SqlPredicate EqualTo(TProp value) => Compare(SqlOperators.Equal, new SqlLiteralExpression(value));
    public SqlPredicate NotEqualTo(TProp value) => Compare(SqlOperators.NotEqual, new SqlLiteralExpression(value));

    public SqlPredicate GreaterThan(TProp value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Compare(SqlOperators.GreaterThan, new SqlLiteralExpression(value));
    }

    public SqlPredicate GreaterThanOrEqual(TProp value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Compare(SqlOperators.GreaterThanOrEqual, new SqlLiteralExpression(value));
    }

    public SqlPredicate LessThan(TProp value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Compare(SqlOperators.LessThan, new SqlLiteralExpression(value));
    }

    public SqlPredicate LessThanOrEqual(TProp value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Compare(SqlOperators.LessThanOrEqual, new SqlLiteralExpression(value));
    }

    public SqlPredicate EqualTo<TOther>(Expression<Func<TEntity, TOther>> other)
        => Compare(SqlOperators.Equal, ColExpression(other));

    public SqlPredicate NotEqualTo<TOther>(Expression<Func<TEntity, TOther>> other)
        => Compare(SqlOperators.NotEqual, ColExpression(other));

    public SqlPredicate GreaterThan<TOther>(Expression<Func<TEntity, TOther>> other)
        => Compare(SqlOperators.GreaterThan, ColExpression(other));

    public SqlPredicate GreaterThanOrEqual<TOther>(Expression<Func<TEntity, TOther>> other)
        => Compare(SqlOperators.GreaterThanOrEqual, ColExpression(other));

    public SqlPredicate LessThan<TOther>(Expression<Func<TEntity, TOther>> other)
        => Compare(SqlOperators.LessThan, ColExpression(other));

    public SqlPredicate LessThanOrEqual<TOther>(Expression<Func<TEntity, TOther>> other)
        => Compare(SqlOperators.LessThanOrEqual, ColExpression(other));

    public SqlPredicate EqualToRawColumn(string columnName)
        => Compare(SqlOperators.Equal, RawColExpression(columnName));

    public SqlPredicate NotEqualToRawColumn(string columnName)
        => Compare(SqlOperators.NotEqual, RawColExpression(columnName));

    public SqlPredicate GreaterThanRawColumn(string columnName)
        => Compare(SqlOperators.GreaterThan, RawColExpression(columnName));

    public SqlPredicate GreaterThanOrEqualRawColumn(string columnName)
        => Compare(SqlOperators.GreaterThanOrEqual, RawColExpression(columnName));

    public SqlPredicate LessThanRawColumn(string columnName)
        => Compare(SqlOperators.LessThan, RawColExpression(columnName));

    public SqlPredicate LessThanOrEqualRawColumn(string columnName)
        => Compare(SqlOperators.LessThanOrEqual, RawColExpression(columnName));

    public SqlPredicate In(params TProp[] values) => In((IEnumerable<TProp>)values);

    public SqlPredicate In(IEnumerable<TProp> values) => BuildIn(values, negated: false);

    public SqlPredicate NotIn(params TProp[] values) => NotIn((IEnumerable<TProp>)values);

    public SqlPredicate NotIn(IEnumerable<TProp> values) => BuildIn(values, negated: true);

    public SqlPredicate Between(TProp lower, TProp upper)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);
        return new SqlPredicate(
            new SqlBetweenExpression(Node, new SqlLiteralExpression(lower), new SqlLiteralExpression(upper),
                Negated: false), Context);
    }

    public SqlPredicate NotBetween(TProp lower, TProp upper)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);
        return new SqlPredicate(
            new SqlBetweenExpression(Node, new SqlLiteralExpression(lower), new SqlLiteralExpression(upper),
                Negated: true), Context);
    }

    public SqlPredicate Between<TMin, TMax>(
        Expression<Func<TEntity, TMin>> minColumn,
        Expression<Func<TEntity, TMax>> maxColumn)
        => new(new SqlBetweenExpression(Node, ColExpression(minColumn), ColExpression(maxColumn), Negated: false),
            Context);

    public SqlPredicate NotBetween<TMin, TMax>(
        Expression<Func<TEntity, TMin>> minColumn,
        Expression<Func<TEntity, TMax>> maxColumn)
        => new(new SqlBetweenExpression(Node, ColExpression(minColumn), ColExpression(maxColumn), Negated: true),
            Context);

    public SqlPredicate BetweenRawColumns(string minColumnName, string maxColumnName)
        => new(
            new SqlBetweenExpression(Node, RawColExpression(minColumnName), RawColExpression(maxColumnName),
                Negated: false), Context);

    public SqlPredicate NotBetweenRawColumns(string minColumnName, string maxColumnName)
        => new(
            new SqlBetweenExpression(Node, RawColExpression(minColumnName), RawColExpression(maxColumnName),
                Negated: true), Context);

    public SqlPredicate IsNull() => new(new SqlNullCheckExpression(Node, Negated: false), Context);

    public SqlPredicate IsNotNull() => new(new SqlNullCheckExpression(Node, Negated: true), Context);

    private SqlPredicate Compare(string op, SqlExpression right)
        => new(new SqlBinaryExpression(Node, op, right), Context);

    private static SqlColumnExpression ColExpression<TOther>(Expression<Func<TEntity, TOther>> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SqlColumnExpression(SqlEntityScope<TEntity>.ExtractNestedMemberPath(other));
    }

    private static SqlColumnExpression RawColExpression(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        return new SqlColumnExpression(columnName);
    }

    private SqlPredicate BuildIn(IEnumerable<TProp> values, bool negated)
    {
        ArgumentNullException.ThrowIfNull(values);
        var items = new List<SqlExpression>();
        foreach (var value in values)
        {
            if (value is null)
            {
                throw new ArgumentException("Collection cannot contain null values.", nameof(values));
            }

            items.Add(new SqlLiteralExpression(value));
        }

        if (items.Count == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", nameof(values));
        }

        return new SqlPredicate(new SqlInExpression(Node, items, negated), Context);
    }
}

public class SqlNumericComparison<TEntity>
{
    internal SqlExpression Node { get; }
    internal SqlRenderContext Context { get; }

    internal SqlNumericComparison(SqlExpression node, SqlRenderContext context)
    {
        Node = node;
        Context = context;
    }

    public SqlPredicate EqualTo(SqlNumericValue value) => Cmp(SqlOperators.Equal, value.Expression);
    public SqlPredicate EqualTo(SqlNumericComparison<TEntity> expression) => Cmp(SqlOperators.Equal, expression.Node);

    public SqlPredicate EqualTo<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Cmp(SqlOperators.Equal, ColExpression(col));

    public SqlPredicate NotEqualTo(SqlNumericValue value) => Cmp(SqlOperators.NotEqual, value.Expression);

    public SqlPredicate NotEqualTo(SqlNumericComparison<TEntity> expression) =>
        Cmp(SqlOperators.NotEqual, expression.Node);

    public SqlPredicate NotEqualTo<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Cmp(SqlOperators.NotEqual, ColExpression(col));

    public SqlPredicate GreaterThan(SqlNumericValue value) => Cmp(SqlOperators.GreaterThan, value.Expression);

    public SqlPredicate GreaterThan(SqlNumericComparison<TEntity> expression) =>
        Cmp(SqlOperators.GreaterThan, expression.Node);

    public SqlPredicate GreaterThan<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Cmp(SqlOperators.GreaterThan, ColExpression(col));

    public SqlPredicate GreaterThanOrEqual(SqlNumericValue value) =>
        Cmp(SqlOperators.GreaterThanOrEqual, value.Expression);

    public SqlPredicate GreaterThanOrEqual(SqlNumericComparison<TEntity> expression) =>
        Cmp(SqlOperators.GreaterThanOrEqual, expression.Node);

    public SqlPredicate GreaterThanOrEqual<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Cmp(SqlOperators.GreaterThanOrEqual, ColExpression(col));

    public SqlPredicate LessThan(SqlNumericValue value) => Cmp(SqlOperators.LessThan, value.Expression);

    public SqlPredicate LessThan(SqlNumericComparison<TEntity> expression) =>
        Cmp(SqlOperators.LessThan, expression.Node);

    public SqlPredicate LessThan<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Cmp(SqlOperators.LessThan, ColExpression(col));

    public SqlPredicate LessThanOrEqual(SqlNumericValue value) => Cmp(SqlOperators.LessThanOrEqual, value.Expression);

    public SqlPredicate LessThanOrEqual(SqlNumericComparison<TEntity> expression) =>
        Cmp(SqlOperators.LessThanOrEqual, expression.Node);

    public SqlPredicate LessThanOrEqual<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Cmp(SqlOperators.LessThanOrEqual, ColExpression(col));

    public SqlPredicate Between(SqlNumericValue lower, SqlNumericValue upper)
        => new(new SqlBetweenExpression(Node, lower.Expression, upper.Expression, Negated: false), Context);

    public SqlPredicate NotBetween(SqlNumericValue lower, SqlNumericValue upper)
        => new(new SqlBetweenExpression(Node, lower.Expression, upper.Expression, Negated: true), Context);

    public SqlPredicate In(params SqlNumericValue[] values)
        => BuildIn(values, negated: false);

    public SqlPredicate NotIn(params SqlNumericValue[] values)
        => BuildIn(values, negated: true);

    private SqlPredicate BuildIn(SqlNumericValue[] values, bool negated)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", nameof(values));
        }

        return new SqlPredicate(
            new SqlInExpression(Node, values.Select(v => (SqlExpression)v.Expression).ToList(), negated), Context);
    }

    private SqlPredicate Cmp(string op, SqlExpression right)
        => new(new SqlBinaryExpression(Node, op, right), Context);

    private protected static SqlColumnExpression ColExpression<TProp>(Expression<Func<TEntity, TProp>> col)
    {
        ArgumentNullException.ThrowIfNull(col);
        return new SqlColumnExpression(SqlEntityScope<TEntity>.ExtractNestedMemberPath(col));
    }
}

public sealed class SqlNumericExpression<TEntity> : SqlNumericComparison<TEntity>
{
    internal SqlNumericExpression(SqlExpression node, SqlRenderContext context)
        : base(node, context)
    {
    }

    public SqlNumericExpression<TEntity> Plus(SqlNumericValue value) => Op(SqlOperators.Add, value.Expression);

    public SqlNumericExpression<TEntity> Plus(SqlNumericComparison<TEntity> expression) =>
        Op(SqlOperators.Add, expression.Node);

    public SqlNumericExpression<TEntity> Plus<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Op(SqlOperators.Add, ColExpression(col));

    public SqlNumericExpression<TEntity> Minus(SqlNumericValue value) => Op(SqlOperators.Subtract, value.Expression);

    public SqlNumericExpression<TEntity> Minus(SqlNumericComparison<TEntity> expression) =>
        Op(SqlOperators.Subtract, expression.Node);

    public SqlNumericExpression<TEntity> Minus<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Op(SqlOperators.Subtract, ColExpression(col));

    public SqlNumericExpression<TEntity> MultiplyBy(SqlNumericValue value) =>
        Op(SqlOperators.Multiply, value.Expression);

    public SqlNumericExpression<TEntity> MultiplyBy(SqlNumericComparison<TEntity> expression) =>
        Op(SqlOperators.Multiply, expression.Node);

    public SqlNumericExpression<TEntity> MultiplyBy<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Op(SqlOperators.Multiply, ColExpression(col));

    public SqlNumericExpression<TEntity> DivideBy(SqlNumericValue value) => Op(SqlOperators.Divide, value.Expression);

    public SqlNumericExpression<TEntity> DivideBy(SqlNumericComparison<TEntity> expression) =>
        Op(SqlOperators.Divide, expression.Node);

    public SqlNumericExpression<TEntity> DivideBy<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Op(SqlOperators.Divide, ColExpression(col));

    public SqlNumericExpression<TEntity> Modulo(SqlNumericValue value) => Op(SqlOperators.Modulo, value.Expression);

    public SqlNumericExpression<TEntity> Modulo(SqlNumericComparison<TEntity> expression) =>
        Op(SqlOperators.Modulo, expression.Node);

    public SqlNumericExpression<TEntity> Modulo<TProp>(Expression<Func<TEntity, TProp>> col)
        where TProp : INumber<TProp>
        => Op(SqlOperators.Modulo, ColExpression(col));

    private SqlNumericExpression<TEntity> Op(string op, SqlExpression right)
        => new(new SqlBinaryExpression(Node, op, right), Context);
}

public class SqlBuiltExpression
{
    internal SqlExpression Node { get; }
    internal SqlRenderContext Context { get; }

    internal SqlBuiltExpression(SqlExpression node, SqlRenderContext context)
    {
        Node = node;
        Context = context;
    }

    public override string ToString() => SqlCheckRenderer.Render(Node, Context);

    public SqlPredicate EqualTo(SqlExpression value) =>
        new(new SqlBinaryExpression(Node, SqlOperators.Equal, value), Context);

    public SqlPredicate NotEqualTo(SqlExpression value) =>
        new(new SqlBinaryExpression(Node, SqlOperators.NotEqual, value), Context);

    public SqlPredicate GreaterThan(SqlExpression value) =>
        new(new SqlBinaryExpression(Node, SqlOperators.GreaterThan, value), Context);

    public SqlPredicate GreaterThanOrEqual(SqlExpression value) =>
        new(new SqlBinaryExpression(Node, SqlOperators.GreaterThanOrEqual, value), Context);

    public SqlPredicate LessThan(SqlExpression value) =>
        new(new SqlBinaryExpression(Node, SqlOperators.LessThan, value), Context);

    public SqlPredicate LessThanOrEqual(SqlExpression value) =>
        new(new SqlBinaryExpression(Node, SqlOperators.LessThanOrEqual, value), Context);

    public SqlPredicate Between(SqlExpression lower, SqlExpression upper)
        => new(new SqlBetweenExpression(Node, lower, upper, Negated: false), Context);

    public SqlPredicate NotBetween(SqlExpression lower, SqlExpression upper)
        => new(new SqlBetweenExpression(Node, lower, upper, Negated: true), Context);

    public SqlPredicate In(params SqlExpression[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", nameof(values));
        }

        return new SqlPredicate(new SqlInExpression(Node, values, Negated: false), Context);
    }

    public SqlPredicate NotIn(params SqlExpression[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", nameof(values));
        }

        return new SqlPredicate(new SqlInExpression(Node, values, Negated: true), Context);
    }

    public static implicit operator string(SqlBuiltExpression expression) => expression.ToString();
    public static implicit operator SqlExpression(SqlBuiltExpression expression) => expression.Node;
}

public sealed class SqlPredicate : SqlBuiltExpression
{
    internal SqlPredicate(SqlExpression node, SqlRenderContext context) : base(node, context)
    {
    }

    public SqlPredicate And(SqlPredicate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SqlPredicate(new SqlLogicalExpression(SqlOperators.And, [Node, other.Node]), Context);
    }

    public SqlPredicate Or(SqlPredicate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SqlPredicate(new SqlLogicalExpression(SqlOperators.Or, [Node, other.Node]), Context);
    }

    public SqlPredicate Not() => new(new SqlNotExpression(Node), Context);
}

public interface IInitializedSqlCase<in TCondition, in TValue, out TResult>
{
    IBuildableSqlCase<TCondition, TValue, TResult> When(TCondition condition, TValue then);
}

public interface IBuildableSqlCase<in TCondition, in TValue, out TResult>
{
    IBuildableSqlCase<TCondition, TValue, TResult> When(TCondition condition, TValue then);
    TResult EndWithElse(TValue elseValue);
    TResult End();
}

internal sealed class SqlCheckCaseBuilder :
    IInitializedSqlCase<SqlPredicate, SqlExpression, SqlBuiltExpression>,
    IBuildableSqlCase<SqlPredicate, SqlExpression, SqlBuiltExpression>
{
    private readonly SqlRenderContext _context;
    private readonly List<(SqlExpression Condition, SqlExpression Then)> _branches = [];

    internal SqlCheckCaseBuilder(SqlRenderContext context) => _context = context;

    public IBuildableSqlCase<SqlPredicate, SqlExpression, SqlBuiltExpression> When(SqlPredicate condition, SqlExpression then)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(then);
        _branches.Add((condition.Node, then));
        return this;
    }

    public SqlBuiltExpression EndWithElse(SqlExpression elseValue)
    {
        ArgumentNullException.ThrowIfNull(elseValue);
        return new SqlBuiltExpression(new SqlCaseExpression(_branches, elseValue), _context);
    }

    public SqlBuiltExpression End()
        => new(new SqlCaseExpression(_branches, Else: null), _context);
}