using System.Collections;
using System.Text;
using SharpPersistence.Abstractions;
using SharpPersistence.Abstractions.Primitives;

namespace SharpPersistence.Parsers.Internals;

internal class ParsedSqlStorage : IParsedSqlStorage
{
    private readonly Dictionary<string, string> _sqlStatements = new(StringComparer.OrdinalIgnoreCase);

    public string this[string tagName]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(tagName);

            if (_sqlStatements.TryGetValue(tagName, out var sqlStatement))
            {
                return sqlStatement;
            }

            throw new InvalidOperationException($"The given tag '{tagName}' is not present in the collection.");
        }
    }

    public bool TryGetParsedSql(string tagName, out string? sql)
    {
        ArgumentNullException.ThrowIfNull(tagName);
        return _sqlStatements.TryGetValue(tagName, out sql);
    }

    internal bool TryAdd(string tagName, string sqlStatement)
    {
        return _sqlStatements.TryAdd(tagName, sqlStatement);
    }

    public IEnumerator<ParsedSql> GetEnumerator()
    {
        foreach (var keyValuePair in _sqlStatements)
        {
            yield return new ParsedSql { Name = keyValuePair.Key, SqlBody = keyValuePair.Value };
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var tagModel in this)
        {
            sb.Append(tagModel.ToString());
        }

        return sb.ToString();
    }

}