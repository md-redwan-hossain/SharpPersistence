using System.Text;

namespace SharpPersistence.Generators;

/// <summary>
/// CASE expression started; at least one WHEN branch is required.
/// </summary>
public interface IInitializedSqlCase : IInitializedSqlCase<string, string, string>
{
    /// <summary>
    /// Adds a <c>WHEN ... THEN ...</c> branch.
    /// </summary>
    new IBuildableSqlCase When(string condition, string then);
}

/// <summary>
/// CASE expression with at least one branch; may add more WHEN branches or terminate.
/// </summary>
public interface IBuildableSqlCase : IBuildableSqlCase<string, string, string>
{
    /// <summary>
    /// Adds a <c>WHEN ... THEN ...</c> branch.
    /// </summary>
    new IBuildableSqlCase When(string condition, string then);

    /// <summary>
    /// Completes the CASE expression with a boolean ELSE branch.
    /// </summary>
    string EndWithElse(bool elseExpression);
}

internal sealed class SqlCaseBuilder : IInitializedSqlCase, IBuildableSqlCase
{
    private readonly List<(string condition, string then)> _branches = [];

    IBuildableSqlCase<string, string, string> IInitializedSqlCase<string, string, string>.When(string condition, string then)
        => When(condition, then);

    IBuildableSqlCase<string, string, string> IBuildableSqlCase<string, string, string>.When(string condition, string then)
        => When(condition, then);

    public IBuildableSqlCase When(string condition, string then)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(then);
        _branches.Add((condition, then));
        return this;
    }

    public string End()
    {
        return BuildSql(elseExpression: null);
    }

    public string EndWithElse(string elseExpression)
    {
        ArgumentNullException.ThrowIfNull(elseExpression);
        return BuildSql(elseExpression);
    }

    public string EndWithElse(bool elseExpression)
    {
        return EndWithElse(elseExpression
            ? bool.TrueString.ToUpperInvariant()
            : bool.FalseString.ToUpperInvariant());
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
