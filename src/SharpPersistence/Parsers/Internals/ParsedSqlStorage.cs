using System.Collections;
using System.Text;
using SharpPersistence.Abstractions;
using SharpPersistence.Abstractions.Primitives;

namespace SharpPersistence.Parsers.Internals;

internal class ParsedSqlStorage : IParsedSqlStorage
{
    private readonly Dictionary<string, string> _sqlStatements = new(StringComparer.OrdinalIgnoreCase);

    public string this[string uniqueTag]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(uniqueTag);

            if (_sqlStatements.TryGetValue(uniqueTag, out var sqlStatement))
            {
                return sqlStatement;
            }

            throw new InvalidOperationException($"The given tag '{uniqueTag}' is not present in the collection.");
        }
    }

    public bool TryGetParsedSql(string uniqueTag, out string? sql)
    {
        ArgumentNullException.ThrowIfNull(uniqueTag);
        return _sqlStatements.TryGetValue(uniqueTag, out sql);
    }

    internal bool TryAdd(string uniqueTag, string sqlStatement)
    {
        return _sqlStatements.TryAdd(uniqueTag, sqlStatement);
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