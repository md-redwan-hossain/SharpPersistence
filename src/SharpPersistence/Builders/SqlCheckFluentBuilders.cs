using System.Linq.Expressions;
using System.Numerics;
using SharpPersistence.Builders.Internal;
using SharpPersistence.Models.Internals;

namespace SharpPersistence;

public sealed class SqlColumnBuilder<TEntity, TProp>
{
    internal SqlColumnExpression Node { get; }
    internal SqlRenderContext Context { get; }

    internal SqlColumnBuilder(SqlColumnExpression node, SqlRenderContext context)
    {
        Node = node;
        Context = context;
    }

    public SqlPredicate EqualTo(TProp value) => Compare("=", new SqlLiteralExpression(value));
    public SqlPredicate NotEqualTo(TProp value) => Compare("<>", new SqlLiteralExpression(value));
    public SqlPredicate GreaterThan(TProp value) => Compare(">", new SqlLiteralExpression(value));
    public SqlPredicate GreaterThanOrEqual(TProp value) => Compare(">=", new SqlLiteralExpression(value));
    public SqlPredicate LessThan(TProp value) => Compare("<", new SqlLiteralExpression(value));
    public SqlPredicate LessThanOrEqual(TProp value) => Compare("<=", new SqlLiteralExpression(value));

    public SqlPredicate EqualToColumn<TOther>(
        Expression<Func<TEntity, TOther>> other,
        bool? delimit = null,
        bool includeParentNames = true)
        => Compare("=", ColExpression(other, delimit, includeParentNames));

    public SqlPredicate NotEqualToColumn<TOther>(
        Expression<Func<TEntity, TOther>> other,
        bool? delimit = null,
        bool includeParentNames = true)
        => Compare("<>", ColExpression(other, delimit, includeParentNames));

    public SqlPredicate GreaterThanColumn<TOther>(
        Expression<Func<TEntity, TOther>> other,
        bool? delimit = null,
        bool includeParentNames = true)
        => Compare(">", ColExpression(other, delimit, includeParentNames));

    public SqlPredicate GreaterThanOrEqualColumn<TOther>(
        Expression<Func<TEntity, TOther>> other,
        bool? delimit = null,
        bool includeParentNames = true)
        => Compare(">=", ColExpression(other, delimit, includeParentNames));

    public SqlPredicate LessThanColumn<TOther>(
        Expression<Func<TEntity, TOther>> other,
        bool? delimit = null,
        bool includeParentNames = true)
        => Compare("<", ColExpression(other, delimit, includeParentNames));

    public SqlPredicate LessThanOrEqualColumn<TOther>(
        Expression<Func<TEntity, TOther>> other,
        bool? delimit = null,
        bool includeParentNames = true)
        => Compare("<=", ColExpression(other, delimit, includeParentNames));

    public SqlPredicate EqualToColumn(string columnName, bool? delimit = null)
        => Compare("=", RawColExpression(columnName, delimit));

    public SqlPredicate NotEqualToColumn(string columnName, bool? delimit = null)
        => Compare("<>", RawColExpression(columnName, delimit));

    public SqlPredicate GreaterThanColumn(string columnName, bool? delimit = null)
        => Compare(">", RawColExpression(columnName, delimit));

    public SqlPredicate GreaterThanOrEqualColumn(string columnName, bool? delimit = null)
        => Compare(">=", RawColExpression(columnName, delimit));

    public SqlPredicate LessThanColumn(string columnName, bool? delimit = null)
        => Compare("<", RawColExpression(columnName, delimit));

    public SqlPredicate LessThanOrEqualColumn(string columnName, bool? delimit = null)
        => Compare("<=", RawColExpression(columnName, delimit));

    public SqlPredicate In(params TProp[] values) => In((IEnumerable<TProp>)values);

    public SqlPredicate In(IEnumerable<TProp> values) => BuildIn(values, negated: false);

    public SqlPredicate NotIn(params TProp[] values) => NotIn((IEnumerable<TProp>)values);

    public SqlPredicate NotIn(IEnumerable<TProp> values) => BuildIn(values, negated: true);

    public SqlPredicate Between(TProp lower, TProp upper)
        => new(new SqlBetweenExpression(Node, new SqlLiteralExpression(lower), new SqlLiteralExpression(upper), Negated: false), Context);

    public SqlPredicate NotBetween(TProp lower, TProp upper)
        => new(new SqlBetweenExpression(Node, new SqlLiteralExpression(lower), new SqlLiteralExpression(upper), Negated: true), Context);

    public SqlPredicate IsNull() => new(new SqlNullCheckExpression(Node, Negated: false), Context);

    public SqlPredicate IsNotNull() => new(new SqlNullCheckExpression(Node, Negated: true), Context);

    private SqlPredicate Compare(string op, SqlExpression right)
        => new(new SqlBinaryExpression(Node, op, right), Context);

    private static SqlColumnExpression ColExpression<TOther>(
        Expression<Func<TEntity, TOther>> other,
        bool? delimit,
        bool includeParentNames)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SqlColumnExpression(SqlEntityScope<TEntity>.ExtractMemberPath(other, includeParentNames), delimit);
    }

    private static SqlColumnExpression RawColExpression(string columnName, bool? delimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        return new SqlColumnExpression(columnName, delimit);
    }

    private SqlPredicate BuildIn(IEnumerable<TProp> values, bool negated)
    {
        ArgumentNullException.ThrowIfNull(values);
        var items = values.Select(v => (SqlExpression)new SqlLiteralExpression(v)).ToList();
        if (items.Count == 0)
        {
            throw new ArgumentException("Collection cannot be empty.", nameof(values));
        }

        return new SqlPredicate(new SqlInExpression(Node, items, negated), Context);
    }
}

public sealed class SqlNumericExpression<TEntity>
{
    internal SqlExpression Node { get; }
    private readonly SqlRenderContext _context;

    internal SqlNumericExpression(SqlExpression node, SqlRenderContext context)
    {
        Node = node;
        _context = context;
    }

    public SqlNumericExpression<TEntity> Plus(SqlNumericValue value) => Op("+", value.Expression);
    public SqlNumericExpression<TEntity> Plus(SqlNumericExpression<TEntity> expression) => Op("+", expression.Node);
    public SqlNumericExpression<TEntity> Plus<TProp>(
        Expression<Func<TEntity, TProp>> col,
        bool? delimit = null,
        bool includeParentNames = true)
        where TProp : INumber<TProp>
        => Op("+", ColExpression(col, delimit, includeParentNames));

    public SqlNumericExpression<TEntity> Minus(SqlNumericValue value) => Op("-", value.Expression);
    public SqlNumericExpression<TEntity> Minus(SqlNumericExpression<TEntity> expression) => Op("-", expression.Node);
    public SqlNumericExpression<TEntity> Minus<TProp>(
        Expression<Func<TEntity, TProp>> col,
        bool? delimit = null,
        bool includeParentNames = true)
        where TProp : INumber<TProp>
        => Op("-", ColExpression(col, delimit, includeParentNames));

    public SqlNumericExpression<TEntity> MultiplyBy(SqlNumericValue value) => Op("*", value.Expression);
    public SqlNumericExpression<TEntity> MultiplyBy(SqlNumericExpression<TEntity> expression) => Op("*", expression.Node);
    public SqlNumericExpression<TEntity> MultiplyBy<TProp>(
        Expression<Func<TEntity, TProp>> col,
        bool? delimit = null,
        bool includeParentNames = true)
        where TProp : INumber<TProp>
        => Op("*", ColExpression(col, delimit, includeParentNames));

    public SqlNumericExpression<TEntity> DivideBy(SqlNumericValue value) => Op("/", value.Expression);
    public SqlNumericExpression<TEntity> DivideBy(SqlNumericExpression<TEntity> expression) => Op("/", expression.Node);
    public SqlNumericExpression<TEntity> DivideBy<TProp>(
        Expression<Func<TEntity, TProp>> col,
        bool? delimit = null,
        bool includeParentNames = true)
        where TProp : INumber<TProp>
        => Op("/", ColExpression(col, delimit, includeParentNames));

    public SqlNumericExpression<TEntity> Modulo(SqlNumericValue value) => Op("%", value.Expression);
    public SqlNumericExpression<TEntity> Modulo(SqlNumericExpression<TEntity> expression) => Op("%", expression.Node);
    public SqlNumericExpression<TEntity> Modulo<TProp>(
        Expression<Func<TEntity, TProp>> col,
        bool? delimit = null,
        bool includeParentNames = true)
        where TProp : INumber<TProp>
        => Op("%", ColExpression(col, delimit, includeParentNames));

    public SqlPredicate EqualTo(SqlNumericValue value) => Cmp("=", value.Expression);
    public SqlPredicate EqualTo(SqlNumericExpression<TEntity> expression) => Cmp("=", expression.Node);
    public SqlPredicate NotEqualTo(SqlNumericValue value) => Cmp("<>", value.Expression);
    public SqlPredicate NotEqualTo(SqlNumericExpression<TEntity> expression) => Cmp("<>", expression.Node);
    public SqlPredicate GreaterThan(SqlNumericValue value) => Cmp(">", value.Expression);
    public SqlPredicate GreaterThan(SqlNumericExpression<TEntity> expression) => Cmp(">", expression.Node);
    public SqlPredicate GreaterThanOrEqual(SqlNumericValue value) => Cmp(">=", value.Expression);
    public SqlPredicate GreaterThanOrEqual(SqlNumericExpression<TEntity> expression) => Cmp(">=", expression.Node);
    public SqlPredicate LessThan(SqlNumericValue value) => Cmp("<", value.Expression);
    public SqlPredicate LessThan(SqlNumericExpression<TEntity> expression) => Cmp("<", expression.Node);
    public SqlPredicate LessThanOrEqual(SqlNumericValue value) => Cmp("<=", value.Expression);
    public SqlPredicate LessThanOrEqual(SqlNumericExpression<TEntity> expression) => Cmp("<=", expression.Node);

    public SqlPredicate Between(SqlNumericValue lower, SqlNumericValue upper)
        => new(new SqlBetweenExpression(Node, lower.Expression, upper.Expression, Negated: false), _context);

    public SqlPredicate NotBetween(SqlNumericValue lower, SqlNumericValue upper)
        => new(new SqlBetweenExpression(Node, lower.Expression, upper.Expression, Negated: true), _context);

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

        return new SqlPredicate(new SqlInExpression(Node, values.Select(v => (SqlExpression)v.Expression).ToList(), negated), _context);
    }

    private SqlNumericExpression<TEntity> Op(string op, SqlExpression right)
        => new(new SqlBinaryExpression(Node, op, right), _context);

    private SqlPredicate Cmp(string op, SqlExpression right)
        => new(new SqlBinaryExpression(Node, op, right), _context);

    private static SqlColumnExpression ColExpression<TProp>(
        Expression<Func<TEntity, TProp>> col,
        bool? delimit,
        bool includeParentNames)
    {
        ArgumentNullException.ThrowIfNull(col);
        return new SqlColumnExpression(SqlEntityScope<TEntity>.ExtractMemberPath(col, includeParentNames), delimit);
    }
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

    public string Build() => SqlCheckRenderer.Render(Node, Context);

    public SqlPredicate EqualTo(SqlExpression value) => new(new SqlBinaryExpression(Node, "=", value), Context);
    public SqlPredicate NotEqualTo(SqlExpression value) => new(new SqlBinaryExpression(Node, "<>", value), Context);

    public static implicit operator string(SqlBuiltExpression expression) => expression.Build();
    public static implicit operator SqlExpression(SqlBuiltExpression expression) => expression.Node;
}

public sealed class SqlPredicate : SqlBuiltExpression
{
    internal SqlPredicate(SqlExpression node, SqlRenderContext context) : base(node, context) { }

    public SqlPredicate And(SqlPredicate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SqlPredicate(new SqlLogicalExpression("AND", [Node, other.Node]), Context);
    }

    public SqlPredicate Or(SqlPredicate other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new SqlPredicate(new SqlLogicalExpression("OR", [Node, other.Node]), Context);
    }

    public SqlPredicate Not() => new(new SqlNotExpression(Node), Context);
}

public sealed class SqlCheckCaseBuilder
{
    private readonly SqlRenderContext _context;
    private readonly List<(SqlExpression Condition, SqlExpression Then)> _branches = [];

    internal SqlCheckCaseBuilder(SqlRenderContext context) => _context = context;

    public SqlCheckCaseBuilder When(SqlExpression condition, SqlExpression then)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(then);
        _branches.Add((condition, then));
        return this;
    }

    public SqlBuiltExpression EndWithElse(SqlExpression elseValue)
    {
        ArgumentNullException.ThrowIfNull(elseValue);
        if (_branches.Count == 0)
        {
            throw new InvalidOperationException("CASE expression requires at least one WHEN branch.");
        }

        return new SqlBuiltExpression(new SqlCaseExpression(_branches, elseValue), _context);
    }

    public SqlBuiltExpression End()
    {
        if (_branches.Count == 0)
        {
            throw new InvalidOperationException("CASE expression requires at least one WHEN branch.");
        }

        return new SqlBuiltExpression(new SqlCaseExpression(_branches, Else: null), _context);
    }
}
