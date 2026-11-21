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

    private SqlRenderContext Context => new(Dialect, IdentifierCase, DelimitIdentifiers);

    public SqlEntityScope<TEntity> For<TEntity>() => new(Context);

    public SqlColumnBuilder<object, TProp> RawColumn<TProp>(string columnName, bool? delimit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        return new SqlColumnBuilder<object, TProp>(new SqlColumnExpression(columnName, delimit), Context);
    }

    public SqlCheckCaseBuilder Case() => new(Context);

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

    internal SqlEntityScope(SqlRenderContext context) => _context = context;

    public SqlColumnBuilder<TEntity, TProp> Column<TProp>(
        Expression<Func<TEntity, TProp>> selector,
        bool? delimit = null,
        bool includeParentNames = true)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var columnName = ExtractMemberPath(selector, includeParentNames);
        return new SqlColumnBuilder<TEntity, TProp>(new SqlColumnExpression(columnName, delimit), _context);
    }

    internal static string ExtractMemberPath<TProp>(
        Expression<Func<TEntity, TProp>> selector,
        bool includeParentNames)
    {
        var current = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } unary
            ? unary.Operand
            : selector.Body;

        var segments = new Stack<string>();
        while (current is MemberExpression member)
        {
            segments.Push(member.Member.Name);
            if (!includeParentNames)
            {
                break;
            }

            current = member.Expression;
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException("Expression must be a property or field access.", nameof(selector));
        }

        return string.Join("_", segments);
    }
}
