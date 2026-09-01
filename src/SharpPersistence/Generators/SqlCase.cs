using System.Text;

namespace SharpPersistence.Generators;

/// <summary>
/// CASE expression started; at least one WHEN branch is required.
/// </summary>
public interface ISqlCaseInitiator
{
    /// <summary>
    /// Adds a <c>WHEN ... THEN ...</c> branch.
    /// </summary>
    ISqlCaseBuildable When(string condition, string then);
}

/// <summary>
/// CASE expression with at least one branch; may add more WHEN branches or terminate.
/// </summary>
public interface ISqlCaseBuildable
{
    /// <summary>
    /// Adds a <c>WHEN ... THEN ...</c> branch.
    /// </summary>
    ISqlCaseBuildable When(string condition, string then);

    /// <summary>
    /// Completes the CASE expression without an ELSE branch.
    /// </summary>
    string End();

    /// <summary>
    /// Completes the CASE expression with an ELSE branch.
    /// </summary>
    string EndWithElse(string elseExpression);
}

internal sealed class SqlCaseBuilder : ISqlCaseInitiator, ISqlCaseBuildable
{
    private const string Indent = "  ";

    private readonly List<(string condition, string then)> _branches = [];

    public ISqlCaseBuildable When(string condition, string then)
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
        var sb = new StringBuilder();
        sb.AppendLine("CASE");

        foreach (var (condition, then) in _branches)
        {
            sb.Append(Indent);
            sb.Append("WHEN ");
            sb.Append(condition);
            sb.Append(" THEN ");
            sb.AppendLine(then);
        }

        if (elseExpression is not null)
        {
            sb.Append(Indent);
            sb.Append("ELSE ");
            sb.AppendLine(elseExpression);
        }

        sb.Append("END");
        return sb.ToString();
    }
}
