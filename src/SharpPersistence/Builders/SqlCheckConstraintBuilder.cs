using System.Linq.Expressions;
using SharpPersistence.Enums;
using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

/// <summary>
/// Fluent builder for generating SQL check-constraint expressions in a type-safe, dialect-aware manner.
/// </summary>
public sealed class SqlCheckConstraintBuilder
{
    public required SqlDialect Dialect { get; init; }
    public required SqlIdentifierCase IdentifierCase { get; init; }
    public bool DelimitIdentifiers { get; init; } = true;
    public string NestedSeparator { get; init; } = "_";

    private SqlRenderContext Context => new(Dialect, IdentifierCase, DelimitIdentifiers);

    public SqlEntityScope<TEntity> For<TEntity>() => new(Context, NestedSeparator);

    public SqlColumnBuilder<object, TProp> RawColumn<TProp>(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        return new SqlColumnBuilder<object, TProp>(new SqlColumnExpression(columnName), Context);
    }

    public IInitializedSqlCase<SqlPredicate, SqlExpression, SqlBuiltExpression> Case() => new SqlCheckCaseBuilder(Context);

    public SqlPredicate And(SqlPredicate first, SqlPredicate second, params SqlPredicate[] others)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(others);

        var operands = new List<SqlExpression>(2 + others.Length) { first.Node, second.Node };
        foreach (var other in others)
        {
            ArgumentNullException.ThrowIfNull(other);
            operands.Add(other.Node);
        }

        return new SqlPredicate(new SqlLogicalExpression(SqlOperators.And, operands), Context);
    }

    public SqlPredicate Or(SqlPredicate first, SqlPredicate second, params SqlPredicate[] others)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(others);

        var operands = new List<SqlExpression>(2 + others.Length) { first.Node, second.Node };
        foreach (var other in others)
        {
            ArgumentNullException.ThrowIfNull(other);
            operands.Add(other.Node);
        }

        return new SqlPredicate(new SqlLogicalExpression(SqlOperators.Or, operands), Context);
    }

    public SqlBuiltExpression Raw(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new SqlBuiltExpression(new SqlRawExpression(sql), Context);
    }
}

public sealed class SqlEntityScope<TEntity>
{
    private readonly SqlRenderContext _context;
    private readonly string _nestedSeparator;

    internal SqlEntityScope(SqlRenderContext context, string nestedSeparator)
    {
        _context = context;
        _nestedSeparator = nestedSeparator;
    }

    public SqlColumnBuilder<TEntity, TProp> Column<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var columnName = ExtractSingleMemberName(selector);
        return new SqlColumnBuilder<TEntity, TProp>(new SqlColumnExpression(columnName), _context);
    }

    public SqlColumnBuilder<TEntity, TProp> NestedColumn<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var columnName = ExtractNestedMemberPath(selector, _nestedSeparator);
        return new SqlColumnBuilder<TEntity, TProp>(new SqlColumnExpression(columnName), _context);
    }

    internal static string ExtractSingleMemberName<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } unary
            ? unary.Operand
            : selector.Body;

        if (body is not MemberExpression member)
        {
            throw new ArgumentException("Expression must be a property or field access.", nameof(selector));
        }

        if (member.Expression is not ParameterExpression)
        {
            throw new ArgumentException("Nested property expressions are not allowed in Column(). Use NestedColumn() instead.", nameof(selector));
        }

        return member.Member.Name;
    }

    internal static string ExtractNestedMemberPath<TProp>(
        Expression<Func<TEntity, TProp>> selector,
        string separator = "_")
    {
        ArgumentNullException.ThrowIfNull(separator);
        var current = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } unary
            ? unary.Operand
            : selector.Body;

        var segments = new Stack<string>();
        while (current is MemberExpression member)
        {
            segments.Push(member.Member.Name);
            current = member.Expression;
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException("Expression must be a property or field access.", nameof(selector));
        }

        return string.Join(separator, segments);
    }
}
