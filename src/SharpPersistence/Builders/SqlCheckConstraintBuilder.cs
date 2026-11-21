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

    private SqlRenderContext Context => new(Dialect, IdentifierCase, DelimitIdentifiers, NestedSeparator);

    /// <summary>
    /// Creates an entity-scoped column builder for <typeparamref name="TEntity"/>.
    /// </summary>
    public SqlEntityScope<TEntity> For<TEntity>() => new(Context);

    /// <summary>
    /// Creates a column builder from an unmapped or raw database column name without applying <see cref="IdentifierCase"/>.
    /// </summary>
    public SqlColumnBuilder<object, TProp> RawColumn<TProp>(string columnName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
        return new SqlColumnBuilder<object, TProp>(new SqlColumnExpression(columnName, ApplyCase: false), Context);
    }

    /// <summary>
    /// Starts a fluent SQL <c>CASE</c> expression.
    /// </summary>
    public IInitializedSqlCase<SqlPredicate, SqlExpression, SqlBuiltExpression> Case() => new SqlCheckCaseBuilder(Context);

    /// <summary>
    /// Combines two or more predicates with SQL <c>AND</c>.
    /// </summary>
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

    /// <summary>
    /// Combines two or more predicates with SQL <c>OR</c>.
    /// </summary>
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

    /// <summary>
    /// Wraps a raw SQL expression fragment.
    /// </summary>
    public SqlBuiltExpression Raw(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new SqlBuiltExpression(new SqlRawExpression(sql), Context);
    }

    /// <summary>
    /// Wraps a raw SQL boolean predicate for use in <see cref="And"/>, <see cref="Or"/>, or <see cref="Case"/>.
    /// </summary>
    public SqlPredicate RawPredicate(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new SqlPredicate(new SqlRawExpression(sql), Context);
    }
}

/// <summary>
/// Provides strongly-typed column selection for <typeparamref name="TEntity"/>.
/// </summary>
public sealed class SqlEntityScope<TEntity>
{
    private readonly SqlRenderContext _context;

    internal SqlEntityScope(SqlRenderContext context) => _context = context;

    /// <summary>
    /// Selects a direct (single-level) property on <typeparamref name="TEntity"/>.
    /// </summary>
    public SqlColumnBuilder<TEntity, TProp> Column<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var columnName = ExtractSingleMemberName(selector);
        return new SqlColumnBuilder<TEntity, TProp>(new SqlColumnExpression(columnName), _context);
    }

    /// <summary>
    /// Selects a nested or owned-type property path on <typeparamref name="TEntity"/>, joined using <see cref="SqlCheckConstraintBuilder.NestedSeparator"/>.
    /// </summary>
    public SqlColumnBuilder<TEntity, TProp> NestedColumn<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var columnName = ExtractNestedMemberPath(selector, _context.NestedSeparator);
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
