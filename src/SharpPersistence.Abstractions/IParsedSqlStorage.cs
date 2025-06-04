using SharpPersistence.Abstractions.Primitives;

namespace SharpPersistence.Abstractions;

public interface IParsedSqlStorage : IEnumerable<ParsedSql>
{
  string this[string tagName] { get; }

  bool TryGetParsedSql(string tagName, out string? sql);
}
