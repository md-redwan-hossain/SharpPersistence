using System.Linq.Expressions;
using SharpPersistence.Enums;
using SharpPersistence.Models.Internals;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace SharpPersistence;
#pragma warning restore IDE0130

/// <summary>
/// Abstraction for building dialect-aware SQL value expressions (such as computed columns) for <typeparamref name="TEntity"/>.
/// </summary>
public interface ISqlExpressionBuilder<TEntity>
{
    SqlColumnBuilder<TEntity, TProp> Column<TProp>(Expression<Func<TEntity, TProp>> selector);
    SqlColumnBuilder<TEntity, TProp> NestedColumn<TProp>(Expression<Func<TEntity, TProp>> selector);
    SqlColumnBuilder<object, TProp> RawColumn<TProp>(string columnName);
    IInitializedSqlCase<SqlPredicate, SqlExpression, SqlBuiltExpression> Case();
    SqlBuiltExpression Raw(string sql);
}

/// <summary>
/// Abstraction for building dialect-aware SQL boolean predicates (such as check constraints and index filters) for <typeparamref name="TEntity"/>.
/// </summary>
public interface ISqlPredicateBuilder<TEntity> : ISqlExpressionBuilder<TEntity>
{
    SqlPredicate And(SqlPredicate first, SqlPredicate second, params SqlPredicate[] others);
    SqlPredicate Or(SqlPredicate first, SqlPredicate second, params SqlPredicate[] others);
    ISqlIfThen If(SqlPredicate condition);
    SqlPredicate RawPredicate(string sql);
}

/// <summary>
/// Fluent builder for generating SQL expressions and predicates in a type-safe, dialect-aware manner.
/// </summary>
public class SqlExpressionBuilder
{
    public required SqlDialect Dialect { get; init; }
    public required SqlIdentifierCase IdentifierCase { get; init; }
    public required bool DelimitIdentifiers { get; init; }
    public string NestedSeparator { get; init; } = "_";

    internal SqlRenderContext Context => new(Dialect, IdentifierCase, DelimitIdentifiers, NestedSeparator);

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
    /// Starts a conditional implication predicate (<c>IF condition THEN requirement</c>), rendered as <c>(NOT (condition) OR requirement)</c>.
    /// </summary>
    public ISqlIfThen If(SqlPredicate condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return new SqlIfBuilder(condition);
    }

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
/// Entity-scoped fluent builder for generating SQL expressions and predicates for <typeparamref name="TEntity"/>
/// in a type-safe, dialect-aware manner.
/// </summary>
public class SqlExpressionBuilder<TEntity> : SqlExpressionBuilder, ISqlPredicateBuilder<TEntity>
{
    /// <summary>
    /// Selects a direct (single-level) property on <typeparamref name="TEntity"/>.
    /// </summary>
    public SqlColumnBuilder<TEntity, TProp> Column<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var columnName = SqlEntityScope<TEntity>.ExtractSingleMemberName(selector);
        return new SqlColumnBuilder<TEntity, TProp>(new SqlColumnExpression(columnName), Context);
    }

    /// <summary>
    /// Selects a nested or owned-type property path on <typeparamref name="TEntity"/>, joined using <see cref="SqlExpressionBuilder.NestedSeparator"/>.
    /// </summary>
    public SqlColumnBuilder<TEntity, TProp> NestedColumn<TProp>(Expression<Func<TEntity, TProp>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var columnName = SqlEntityScope<TEntity>.ExtractNestedMemberPath(selector, Context.NestedSeparator);
        return new SqlColumnBuilder<TEntity, TProp>(new SqlColumnExpression(columnName), Context);
    }
}
