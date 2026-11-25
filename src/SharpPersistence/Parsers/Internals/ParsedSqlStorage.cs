using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SharpPersistence.Abstractions;
using SharpPersistence.Abstractions.ValueObjects;

namespace SharpPersistence.Parsers.Internals;

internal sealed class ParsedSqlStorage : IParsedSqlStorage
{
    private readonly Dictionary<string, string> _sqlStatements = new(StringComparer.OrdinalIgnoreCase);
    private FrozenDictionary<string, string>? _frozen;

    public string this[string uniqueTag]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(uniqueTag);

            if (TryLookup(uniqueTag, out var sqlStatement))
            {
                return sqlStatement;
            }

            throw new InvalidOperationException($"The given tag '{uniqueTag}' is not present in the collection.");
        }
    }

    public bool TryGetParsedSql(string uniqueTag, out string? sql)
    {
        ArgumentNullException.ThrowIfNull(uniqueTag);
        return TryLookup(uniqueTag, out sql);
    }

    internal bool TryAdd(string uniqueTag, string sqlStatement)
    {
        _frozen = null;
        return _sqlStatements.TryAdd(uniqueTag, sqlStatement);
    }

    internal void Freeze()
    {
        _frozen = _sqlStatements.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private bool TryLookup(string uniqueTag, [NotNullWhen(true)] out string? sql) =>
        _frozen is not null
            ? _frozen.TryGetValue(uniqueTag, out sql)
            : _sqlStatements.TryGetValue(uniqueTag, out sql);

    public IEnumerator<ParsedSql> GetEnumerator()
    {
        if (_frozen is not null)
        {
            foreach (var keyValuePair in _frozen)
            {
                yield return new ParsedSql { Name = keyValuePair.Key, SqlBody = keyValuePair.Value };
            }

            yield break;
        }

        foreach (var keyValuePair in _sqlStatements)
        {
            yield return new ParsedSql { Name = keyValuePair.Key, SqlBody = keyValuePair.Value };
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

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