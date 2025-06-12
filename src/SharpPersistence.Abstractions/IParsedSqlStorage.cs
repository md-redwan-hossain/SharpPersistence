using SharpPersistence.Abstractions.ValueObjects;

namespace SharpPersistence.Abstractions;

public interface IParsedSqlStorage : IEnumerable<ParsedSql>
{
  string this[string uniqueTag] { get; }

  bool TryGetParsedSql(string uniqueTag, out string? sql);
}
