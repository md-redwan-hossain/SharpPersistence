using System.Linq.Expressions;
using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

/// <summary>
/// Fluent builder for generating SQL check-constraint expressions in a type-safe, dialect-aware manner.
/// </summary>
public sealed class SqlCheckConstraintBuilder : SqlExpressionBuilder;

/// <summary>
/// Entity-scoped fluent builder for generating SQL check-constraint expressions for <typeparamref name="TEntity"/>
/// in a type-safe, dialect-aware manner.
/// </summary>
public sealed class SqlCheckConstraintBuilder<TEntity> : SqlExpressionBuilder<TEntity>;

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
    /// Selects a nested or owned-type property path on <typeparamref name="TEntity"/>, joined using <see cref="SqlExpressionBuilder.NestedSeparator"/>.
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
