using System.Text;

namespace SharpPersistence.Generators;

/// <summary>
/// CASE expression started; at least one WHEN branch is required.
/// </summary>
public interface IInitializedSqlCase
{
    /// <summary>
    /// Adds a <c>WHEN ... THEN ...</c> branch.
    /// </summary>
    IBuildableSqlCase When(string condition, string then);
}

/// <summary>
/// CASE expression with at least one branch; may add more WHEN branches or terminate.
/// </summary>
public interface IBuildableSqlCase
{
    /// <summary>
    /// Adds a <c>WHEN ... THEN ...</c> branch.
    /// </summary>
    IBuildableSqlCase When(string condition, string then);

    /// <summary>
    /// Completes the CASE expression without an ELSE branch.
    /// </summary>
    string End();

    /// <summary>
    /// Completes the CASE expression with an ELSE branch.
    /// </summary>
    string EndWithElse(string elseExpression);
}

internal sealed class SqlCaseBuilder : IInitializedSqlCase, IBuildableSqlCase
{
    private readonly List<(string condition, string then)> _branches = [];

    public IBuildableSqlCase When(string condition, string then)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(then);
        _branches.Add((condition, then));
        return this;
    }

    public string End() => BuildSql(elseExpression: null);

    public string EndWithElse(string elseExpression)
    {
        ArgumentNullException.ThrowIfNull(elseExpression);
        return BuildSql(elseExpression);
    }

    private string BuildSql(string? elseExpression)
    {
        var sb = new StringBuilder("CASE");

        foreach (var (condition, then) in _branches)
        {
            sb.Append(" WHEN ");
            sb.Append(condition);
            sb.Append(" THEN ");
            sb.Append(then);
        }

        if (elseExpression is not null)
        {
            sb.Append(" ELSE ");
            sb.Append(elseExpression);
        }

        sb.Append(" END");
        return sb.ToString();
    }
}
